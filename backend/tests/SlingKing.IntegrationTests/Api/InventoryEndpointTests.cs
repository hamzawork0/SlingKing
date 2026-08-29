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

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class InventoryEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public InventoryEndpointTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await AuthenticateAsync();
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

    private record MovementView(
        long Id, DateTimeOffset OccurredAt, string Type, int PhysicalDelta,
        decimal? TotalCost, string? Reason);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    /// <summary>
    /// A fresh product with one variant, so every test starts from a variant nothing else
    /// has touched. Sharing one variant across tests would make the running totals depend
    /// on test order.
    /// </summary>
    private async Task<long> NewVariantAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Stock {Guid.NewGuid():N}"[..16], null, null,
            [new VariantRequest(null, "Tan", "Standard", 4500m, 5, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductView>();
        return product!.Variants.Single().Id;
    }

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Recording_a_movement_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(1, "OpeningStock", 1, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Opening_stock_raises_the_level_and_opens_one_cost_layer()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 10, 120m, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();
        movement!.PhysicalDelta.ShouldBe(10);
        movement.Type.ShouldBe("OpeningStock");

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(10);

        // Reservations are R2. Nothing in this milestone may move this number.
        level.ReservedQty.ShouldBe(0);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId).ToListAsync());
        layers.Count.ShouldBe(1);
        layers[0].OriginalQty.ShouldBe(10);
        layers[0].RemainingQty.ShouldBe(10);
        layers[0].UnitCost.ShouldBe(120m);
    }

    [Fact]
    public async Task Opening_stock_may_have_no_cost_at_all()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 4, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantId));

        // Unknown, not zero. Zero here would silently claim the bags were free.
        layer.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task Two_openings_stack_into_two_layers_in_arrival_order()
    {
        var variantId = await NewVariantAsync();

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 3, 100m, null));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 5, 200m, null));

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(8);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync());

        layers.Select(l => l.UnitCost).ShouldBe([100m, 200m]);
    }

    [Fact]
    public async Task A_quantity_of_zero_is_refused()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 0, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_variant_is_not_found()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(999_999_999, "OpeningStock", 1, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A missing route is also a 404, so assert the authored body rather than the status
        // alone — otherwise this test passes just as well with no endpoint at all.
        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task A_type_this_endpoint_does_not_accept_is_refused_by_name()
    {
        var variantId = await NewVariantAsync();

        foreach (var type in new[] { "Purchase", "Sale", "Adjustment" })
        {
            var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
                new MovementRequest(variantId, type, 1, null, null));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"type {type}");
        }
    }
}
