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
/// Two purchases touching the same two variants in opposite line orders. Without a consistent
/// lock order each holds the row the other wants and Postgres kills one as a deadlock.
/// </summary>
[Collection("database")]
public class PurchaseConcurrencyTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _first;
    private readonly HttpClient _second;

    public PurchaseConcurrencyTests(SlingKingApiFactory factory)
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
    private record SupplierOption(int Id, string Name, string? Phone);
    private record SupplierRequest(string Name, string? Phone);
    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);
    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);
    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);
    private record PurchaseLineRequest(long VariantId, int Quantity, decimal? UnitCost);
    private record PurchaseRequest(
        int SupplierId, DateTimeOffset PurchaseDate, string? Notes,
        List<PurchaseLineRequest> Lines);

    private async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    [Fact]
    public async Task Two_purchases_touching_the_same_variants_in_opposite_orders_both_complete()
    {
        var supplierResponse = await _first.PostAsJsonAsync("/api/v1/suppliers",
            new SupplierRequest($"Sup {Guid.NewGuid():N}"[..14], null));
        var supplierId = (await supplierResponse.Content
            .ReadFromJsonAsync<SupplierOption>())!.Id;

        var productResponse = await _first.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Race {Guid.NewGuid():N}"[..15], null, null,
            [
                new VariantRequest(null, "Tan", "Standard", 4500m, 5, true),
                new VariantRequest(null, "Black", "Standard", 4500m, 5, true)
            ]));

        var product = await productResponse.Content.ReadFromJsonAsync<ProductView>();
        var a = product!.Variants[0].Id;
        var b = product.Variants[1].Id;

        // The orders are deliberately opposite. The service sorts by variant id before
        // locking, so the request order must not matter.
        var ascending = _first.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(a, 3, 100m), new PurchaseLineRequest(b, 3, 100m)]));

        var descending = _second.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(b, 5, 200m), new PurchaseLineRequest(a, 5, 200m)]));

        var results = await Task.WhenAll(ascending, descending);

        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both purchases landed in full, so both variants hold 3 + 5.
        var levels = await db.StockLevels.AsNoTracking()
            .Where(s => s.VariantId == a || s.VariantId == b)
            .OrderBy(s => s.VariantId)
            .ToListAsync();

        levels.Select(l => l.PhysicalQty).ShouldBe([8, 8]);
    }
}
