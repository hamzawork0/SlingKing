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
    private record PurchaseView(long Id);

    private async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    // CORRECTION (whole-branch review, r1-m3-purchases): the commit that introduced this test
    // ("test: two purchases locking the same variants in opposite orders", d488804) reported
    // five clean runs with the OrderBy removed as a null result -- as if a valid experiment had
    // simply failed to reproduce a deadlock. It was not a valid experiment. PurchaseService
    // .RecordAsync opens its transaction and immediately calls NextAsync("purchase"), an UPDATE
    // on the single number_sequences row for that key, on the same connection -- so it takes an
    // exclusive lock on that one row and holds it to COMMIT. Two concurrent POST /purchases are
    // therefore fully serialised on the sequence row before either one reaches a stock_levels
    // lock at all: this test cannot deadlock with the ascending-variant-id sort removed, and it
    // cannot deadlock with it in place either. Five (or five hundred) clean runs here prove
    // nothing about the sort -- do not use a green run of THIS test to justify deleting the
    // OrderBy in ApplyPurchaseAsync.
    //
    // PurchaseService.CancelAsync allocates no purchase number and so takes no sequence lock,
    // which means two concurrent cancels genuinely can hold two stock_levels row locks at once.
    // Two_cancels_touching_the_same_variants_in_opposite_orders_both_complete below is the test
    // that can actually fail if ReversePurchaseAsync's ordering is ever removed.
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

    /// <summary>
    /// The race that can actually happen: CancelAsync allocates no purchase number, so it takes
    /// no number_sequences lock and two concurrent cancels can each be holding one stock_levels
    /// row while wanting the other's, in opposite orders, at the same instant. This is what
    /// ReversePurchaseAsync's ascending-variant-id sort actually has to defend against -- unlike
    /// the record-vs-record test above, which cannot deadlock either way.
    /// </summary>
    [Fact]
    public async Task Two_cancels_touching_the_same_variants_in_opposite_orders_both_complete()
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

        // Record both purchases first, in opposite line orders, so each opens its own cost
        // layer over both variants. Recording is serialised by the purchase-number sequence
        // lock regardless of order, so there is nothing to race here -- sequential is fine.
        var firstPurchase = await _first.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(a, 3, 100m), new PurchaseLineRequest(b, 3, 100m)]));
        var firstId = (await firstPurchase.Content.ReadFromJsonAsync<PurchaseView>())!.Id;

        var secondPurchase = await _second.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(b, 5, 200m), new PurchaseLineRequest(a, 5, 200m)]));
        var secondId = (await secondPurchase.Content.ReadFromJsonAsync<PurchaseView>())!.Id;

        // Neither cancel allocates a purchase number, so nothing serialises these two ahead of
        // the stock_levels locks -- this is the actual race.
        var cancelFirst = _first.PostAsync($"/api/v1/purchases/{firstId}/cancel", null);
        var cancelSecond = _second.PostAsync($"/api/v1/purchases/{secondId}/cancel", null);

        var results = await Task.WhenAll(cancelFirst, cancelSecond);

        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both purchases were fully reversed, so both variants are back to nothing.
        var levels = await db.StockLevels.AsNoTracking()
            .Where(s => s.VariantId == a || s.VariantId == b)
            .OrderBy(s => s.VariantId)
            .ToListAsync();

        levels.Select(l => l.PhysicalQty).ShouldBe([0, 0]);
    }
}
