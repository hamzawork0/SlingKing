using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

/// <summary>
/// You are one person, but you have two browser tabs. Both read "12 in stock", both post an
/// outflow of 8. Neither request is wrong on its own; the second must see the first's result.
/// </summary>
[Collection("database")]
public class StockConcurrencyTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _first;
    private readonly HttpClient _second;

    public StockConcurrencyTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _first = factory.CreateClient();
        _second = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await AuthenticateAsync(_first);
        await AuthenticateAsync(_second);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string AccessToken);
    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);
    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);
    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);
    private record MovementRequest(
        long VariantId, string Type, int Quantity, decimal? UnitCost, string? Reason);
    private record RecountRequest(int CountedQuantity);

    private async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<long> NewVariantWithStockAsync(int quantity)
    {
        var created = await _first.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Race {Guid.NewGuid():N}"[..15], null, null,
            [new VariantRequest(null, "Tan", "Standard", 4500m, 5, true)]));

        var product = await created.Content.ReadFromJsonAsync<ProductView>();
        var variantId = product!.Variants.Single().Id;

        var opened = await _first.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", quantity, 100m, null));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created);

        return variantId;
    }

    private async Task<int> LevelAsync(long variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var level = await db.StockLevels.AsNoTracking()
            .SingleAsync(s => s.VariantId == variantId);
        return level.PhysicalQty;
    }

    [Fact]
    public async Task Two_outflows_racing_for_the_same_stock_cannot_both_win()
    {
        var variantId = await NewVariantWithStockAsync(12);

        Task<HttpResponseMessage> Damage(HttpClient client) => client.PostAsJsonAsync(
            "/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 8, null, "Water damaged in transit"));

        var results = await Task.WhenAll(Damage(_first), Damage(_second));

        // One takes 8 of 12; the other must find 4 and refuse. Both succeeding would drive the
        // level to -4, which ck_physical_non_negative would abort anyway — this asserts the
        // refusal is a clean 409 rather than a 500 out of a constraint violation.
        results.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        (await LevelAsync(variantId)).ShouldBe(4);
    }

    [Fact]
    public async Task Two_recounts_racing_do_not_lose_one_of_them()
    {
        var variantId = await NewVariantWithStockAsync(12);

        // The case the check constraint cannot see. Both counts are non-negative and both are
        // legal, so nothing at the database refuses them — without the lock the second
        // computes its delta from the stale 12 and silently overwrites the first.
        var toNine = _second.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(9));
        var toFive = _first.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(5));

        var results = await Task.WhenAll(toNine, toFive);
        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        // Whichever ran second is authoritative, so the level is 9 or 5 — but the deltas must
        // add up to it from 12. A lost update leaves the ledger summing to something else.
        var level = await LevelAsync(variantId);
        level.ShouldBeOneOf(9, 5);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var summed = await db.InventoryTransactions
            .Where(t => t.VariantId == variantId)
            .SumAsync(t => t.PhysicalDelta);

        summed.ShouldBe(level);
    }
}
