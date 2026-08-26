using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class ProductEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public ProductEndpointTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string AccessToken);

    protected record VariantPayload(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);

    protected record ProductPayload(
        string Name, int? CategoryId, string? Description, List<VariantPayload> Variants);

    protected record VariantView(
        long Id, string Sku, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive, int PhysicalQty);

    protected record ProductView(
        long Id, string Code, string Name, int? CategoryId, string? CategoryName,
        string? Description, bool IsActive, int TotalStock, List<VariantView> Variants);

    protected async Task<ProductView> CreateAsync(ProductPayload payload)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", payload);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductView>())!;
    }

    private static ProductPayload TwoColours(string name) => new(
        name, null, null,
        [
            new VariantPayload(null, "Tan", null, 1850m, 2, true),
            new VariantPayload(null, "Black", null, 1850m, 2, true)
        ]);

    [Fact]
    public async Task Creating_a_product_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/products", TwoColours("Unauthorised Sling"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_created_product_gets_a_code_and_its_variants_get_skus()
    {
        var product = await CreateAsync(TwoColours("Leather Sling"));

        product.Code.ShouldStartWith("BAG-");
        product.Code.Length.ShouldBe(10);
        product.Variants.Count.ShouldBe(2);
        product.Variants[0].Sku.ShouldBe($"{product.Code}-V01");
        product.Variants[1].Sku.ShouldBe($"{product.Code}-V02");
        product.IsActive.ShouldBeTrue();
        product.TotalStock.ShouldBe(0);
    }

    /// <summary>
    /// The allocator's gap-free behaviour under concurrency is already proven by
    /// NumberSequenceTests with 20 concurrent callers. What is unproven here is only that
    /// ProductService is wired to the allocator at all, which two sequential creates settle.
    /// </summary>
    [Fact]
    public async Task Consecutive_products_take_consecutive_codes()
    {
        var first = await CreateAsync(TwoColours("Sequential One"));
        var second = await CreateAsync(TwoColours("Sequential Two"));

        var firstNumber = int.Parse(first.Code["BAG-".Length..]);
        var secondNumber = int.Parse(second.Code["BAG-".Length..]);

        secondNumber.ShouldBe(firstNumber + 1);
    }

    [Fact]
    public async Task A_product_with_no_variants_gets_one_default_variant()
    {
        var product = await CreateAsync(new ProductPayload("Plain Tote", null, null, []));

        product.Variants.Count.ShouldBe(1);
        product.Variants[0].Colour.ShouldBeNull();
        product.Variants[0].Size.ShouldBeNull();
        product.Variants[0].Sku.ShouldBe($"{product.Code}-V01");
    }

    /// <summary>
    /// Stock exists only at variant level, so a variant with no stock row could never hold
    /// stock. Writing the row eagerly is also what lets TotalStock read a real row in M2b
    /// rather than a COALESCE over an absent one.
    /// </summary>
    [Fact]
    public async Task Every_new_variant_gets_a_zero_stock_row_at_the_default_location()
    {
        var product = await CreateAsync(TwoColours("Stock Row Sling"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var defaultLocationId = await db.Locations
            .Where(l => l.IsDefault).Select(l => l.Id).SingleAsync();

        foreach (var variant in product.Variants)
        {
            var level = await db.StockLevels
                .SingleOrDefaultAsync(s => s.VariantId == variant.Id);

            level.ShouldNotBeNull();
            level.LocationId.ShouldBe(defaultLocationId);
            level.PhysicalQty.ShouldBe(0);
            level.ReservedQty.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Two_variants_with_the_same_colour_and_size_are_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", new ProductPayload(
            "Clashing Sling", null, null,
            [
                new VariantPayload(null, "Tan", null, 1850m, 2, true),
                new VariantPayload(null, "tan", null, 1900m, 2, true)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_category_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products",
            new ProductPayload("Orphan Sling", 999_999, null,
                [new VariantPayload(null, "Tan", null, 100m, 0, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_negative_price_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products",
            new ProductPayload("Negative Sling", null, null,
                [new VariantPayload(null, "Tan", null, -1m, 0, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_negative_low_stock_threshold_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products",
            new ProductPayload("Threshold Sling", null, null,
                [new VariantPayload(null, "Tan", null, 100m, -1, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A rejected create must leave nothing behind: no product row, and no gap in the code
    /// sequence.
    ///
    /// Be precise about what this proves. Duplicate combinations are rejected BEFORE the
    /// transaction opens, so this path never allocates a number in the first place — the
    /// no-gap assertion holds because nothing was taken, not because a rollback returned it.
    /// That the allocator runs inside the transaction (so a failure AFTER allocation also
    /// returns its number) is asserted by code review, not by this test: forcing a failure
    /// after allocation would need fault injection that is not worth its weight here.
    /// Both halves are still worth pinning — a future refactor that allocates before
    /// validating would break the no-gap half immediately.
    /// </summary>
    [Fact]
    public async Task A_rejected_create_leaves_no_product_behind()
    {
        var before = await CreateAsync(TwoColours("Before Rollback"));

        await _client.PostAsJsonAsync("/api/v1/products", new ProductPayload(
            "Doomed Sling", null, null,
            [
                new VariantPayload(null, "Tan", null, 100m, 0, true),
                new VariantPayload(null, "Tan", null, 100m, 0, true)
            ]));

        var after = await CreateAsync(TwoColours("After Rollback"));

        var beforeNumber = int.Parse(before.Code["BAG-".Length..]);
        var afterNumber = int.Parse(after.Code["BAG-".Length..]);

        afterNumber.ShouldBe(beforeNumber + 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        (await db.Products.AnyAsync(p => p.Name == "Doomed Sling")).ShouldBeFalse();
    }
}
