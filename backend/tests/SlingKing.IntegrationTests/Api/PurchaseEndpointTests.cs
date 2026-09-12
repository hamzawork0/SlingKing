using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Domain.Common;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class PurchaseEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public PurchaseEndpointTests(SlingKingApiFactory factory)
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

    private record PurchaseLineView(
        long Id, long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int Quantity, decimal? UnitCost, decimal? LineTotal);
    private record PurchaseView(
        long Id, string Number, int SupplierId, string SupplierName,
        DateTimeOffset PurchaseDate, string Status, decimal? TotalAmount, string? Notes,
        List<PurchaseLineView> Lines);

    private record MovementRequest(
        long VariantId, string Type, int Quantity, decimal? UnitCost, string? Reason);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<int> NewSupplierAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/suppliers",
            new SupplierRequest($"Sup {Guid.NewGuid():N}"[..14], null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SupplierOption>())!.Id;
    }

    /// <summary>
    /// A fresh product with <paramref name="colours"/> variants, so every test starts from
    /// stock nothing else has touched. Sharing variants across tests would make the running
    /// totals depend on test order.
    /// </summary>
    private async Task<(long ProductId, List<long> VariantIds)> NewProductAsync(
        params string[] colours)
    {
        var variants = colours
            .Select(c => new VariantRequest(null, c, null, 4500m, 5, true))
            .ToList();

        var response = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Buy {Guid.NewGuid():N}"[..14], null, null, variants));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = await response.Content.ReadFromJsonAsync<ProductView>();
        return (product!.Id, product.Variants.Select(v => v.Id).ToList());
    }

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static DateTimeOffset Today => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Recording_a_purchase_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(1, Today, null, [new PurchaseLineRequest(1, 1, null)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_purchase_raises_stock_and_opens_one_layer_per_line()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan", "Black");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, "Karol Bagh trip",
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[1], 8, 480m)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();
        purchase!.Number.ShouldStartWith("PUR-");
        purchase.Status.ShouldBe("Completed");
        purchase.TotalAmount.ShouldBe(9240m);
        purchase.Lines.Count.ShouldBe(2);
        purchase.Lines.Single(l => l.VariantId == variantIds[0]).LineTotal.ShouldBe(5400m);

        var levels = await QueryAsync(db => db.StockLevels
            .Where(s => variantIds.Contains(s.VariantId))
            .OrderBy(s => s.VariantId)
            .ToListAsync());

        levels.Select(l => l.PhysicalQty).ShouldBe([12, 8]);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => variantIds.Contains(l.VariantId))
            .OrderBy(l => l.VariantId)
            .ToListAsync());

        layers.Count.ShouldBe(2);
        layers.ShouldAllBe(l => l.SourceType == CostLayerSourceType.Purchase);
        layers.Select(l => l.UnitCost).ShouldBe([450m, 480m]);
        layers.ShouldAllBe(l => l.RemainingQty == l.OriginalQty);
    }

    [Fact]
    public async Task Every_ledger_row_points_back_at_the_purchase()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 5, 100m)]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();

        var ledger = await QueryAsync(db => db.InventoryTransactions
            .SingleAsync(t => t.VariantId == variantIds[0]));

        ledger.Type.ShouldBe(InventoryTransactionType.Purchase);
        ledger.PhysicalDelta.ShouldBe(5);
        ledger.UnitCost.ShouldBe(100m);
        ledger.TotalCost.ShouldBe(500m);

        // Without these an item's history cannot explain where the bags came from.
        ledger.ReferenceType.ShouldBe(InventoryReferenceType.Purchase);
        ledger.ReferenceId.ShouldBe(purchase!.Id);
    }

    [Fact]
    public async Task A_layer_records_which_purchase_line_made_it()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 5, 100m)]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();
        var lineId = purchase!.Lines.Single().Id;

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantIds[0]));

        // Cancellation finds its own layers through this. Pointing at the ledger row instead
        // would work too, but a layer that names its purchase line is self-describing.
        layer.SourceId.ShouldBe(lineId);
    }

    [Fact]
    public async Task Two_purchases_take_consecutive_numbers()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var first = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));
        var second = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        var a = await first.Content.ReadFromJsonAsync<PurchaseView>();
        var b = await second.Content.ReadFromJsonAsync<PurchaseView>();

        var firstNumber = int.Parse(a!.Number["PUR-".Length..]);
        var secondNumber = int.Parse(b!.Number["PUR-".Length..]);

        secondNumber.ShouldBe(firstNumber + 1);
    }

    [Fact]
    public async Task An_uncosted_line_makes_the_whole_purchase_total_unknown()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan", "Black");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[1], 8, null)
            ]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();

        // 5400 is the sum of the line we know. Reporting it would understate what was paid.
        purchase!.TotalAmount.ShouldBeNull();
        purchase.Lines.Single(l => l.VariantId == variantIds[1]).LineTotal.ShouldBeNull();

        var uncosted = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantIds[1]));

        uncosted.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_variant_twice_is_refused_by_name()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[0], 3, 450m)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString()
            .ShouldBe("Tan appears twice. Put the whole quantity on one line.");
    }

    [Fact]
    public async Task A_purchase_with_no_lines_is_refused()
    {
        var supplierId = await NewSupplierAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null, []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_supplier_is_not_found()
    {
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(999_999, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That supplier no longer exists.");
    }

    [Fact]
    public async Task A_purchase_against_a_retired_product_is_not_found()
    {
        var supplierId = await NewSupplierAsync();
        var (productId, variantIds) = await NewProductAsync("Tan");

        (await _client.PostAsync($"/api/v1/products/{productId}/deactivate", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task Nothing_is_written_when_a_purchase_is_refused()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var before = await QueryAsync(db => db.Purchases.CountAsync());

        await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[0], 3, 450m)
            ]));

        (await QueryAsync(db => db.Purchases.CountAsync())).ShouldBe(before);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));
        level.PhysicalQty.ShouldBe(0);
    }

    [Fact]
    public async Task Back_dating_a_purchase_does_not_move_it_ahead_in_the_cost_queue()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        // Stock already on hand at a known cost.
        var opening = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantIds[0], "OpeningStock", 4, 100m, null));
        opening.StatusCode.ShouldBe(HttpStatusCode.Created);

        // A purchase dated a month ago, recorded now.
        var purchase = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, DateTimeOffset.UtcNow.AddMonths(-1), null,
                [new PurchaseLineRequest(variantIds[0], 4, 900m)]));
        purchase.StatusCode.ShouldBe(HttpStatusCode.Created);

        // FIFO must still take the opening stock first. If received_at had followed the
        // purchase date, this damage would cost 900 apiece and every cost already recorded
        // against this variant would silently have become wrong.
        var damage = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantIds[0], "Damage", 4, null, "Water damaged in transit"));

        damage.StatusCode.ShouldBe(HttpStatusCode.Created);

        var ledger = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantIds[0]
                     && t.Type == InventoryTransactionType.Damage)
            .SingleAsync());

        ledger.TotalCost.ShouldBe(400m);
    }
}
