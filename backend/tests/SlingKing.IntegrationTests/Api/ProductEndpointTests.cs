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

    private async Task<List<ProductView>> ListAsync(string query = "")
    {
        var products = await _client.GetFromJsonAsync<List<ProductView>>(
            $"/api/v1/products{query}");

        return products!;
    }

    [Fact]
    public async Task A_created_product_appears_in_the_list()
    {
        var created = await CreateAsync(TwoColours($"Listed {Guid.NewGuid():N}"[..20]));

        var products = await ListAsync();

        products.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Search_matches_the_product_name()
    {
        var marker = $"Findable{Guid.NewGuid():N}"[..18];
        var created = await CreateAsync(TwoColours(marker));

        var products = await ListAsync($"?search={marker}");

        products.Count.ShouldBe(1);
        products[0].Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Search_matches_the_product_code()
    {
        var created = await CreateAsync(TwoColours($"ByCode {Guid.NewGuid():N}"[..18]));

        var products = await ListAsync($"?search={created.Code}");

        products.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Search_matches_a_variant_sku()
    {
        var created = await CreateAsync(TwoColours($"BySku {Guid.NewGuid():N}"[..18]));

        var products = await ListAsync($"?search={created.Variants[1].Sku}");

        products.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Search_is_case_insensitive()
    {
        var marker = $"CaseTest{Guid.NewGuid():N}"[..18];
        await CreateAsync(TwoColours(marker));

        var products = await ListAsync($"?search={marker.ToUpper()}");

        products.Count.ShouldBe(1);
    }

    private async Task<ProductView> UpdateAsync(long id, ProductPayload payload)
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/products/{id}", payload);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProductView>())!;
    }

    private static VariantPayload Keep(VariantView v, decimal? price = null, bool? active = null) =>
        new(v.Id, v.Colour, v.Size, price ?? v.SellingPrice, v.LowStockThreshold,
            active ?? v.IsActive);

    [Fact]
    public async Task Updating_changes_the_name_and_keeps_the_code()
    {
        var product = await CreateAsync(TwoColours("Original Name"));

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            "Renamed", null, "Now with a description",
            product.Variants.Select(v => Keep(v)).ToList()));

        updated.Name.ShouldBe("Renamed");
        updated.Description.ShouldBe("Now with a description");
        updated.Code.ShouldBe(product.Code);
    }

    [Fact]
    public async Task An_existing_variant_is_updated_in_place_and_keeps_its_sku()
    {
        var product = await CreateAsync(TwoColours("Reprice Me"));
        var tan = product.Variants[0];

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [Keep(tan, price: 1950m), Keep(product.Variants[1])]));

        var repriced = updated.Variants.Single(v => v.Id == tan.Id);
        repriced.SellingPrice.ShouldBe(1950m);
        repriced.Sku.ShouldBe(tan.Sku);
    }

    [Fact]
    public async Task A_variant_with_no_id_is_created_and_takes_the_next_ordinal()
    {
        var product = await CreateAsync(TwoColours("Growing Range"));

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [
                Keep(product.Variants[0]),
                Keep(product.Variants[1]),
                new VariantPayload(null, "Brown", null, 1900m, 1, true)
            ]));

        updated.Variants.Count.ShouldBe(3);
        updated.Variants.Single(v => v.Colour == "Brown").Sku.ShouldBe($"{product.Code}-V03");
    }

    [Fact]
    public async Task A_variant_created_by_an_update_also_gets_a_stock_row()
    {
        var product = await CreateAsync(TwoColours("Late Variant"));

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [
                Keep(product.Variants[0]),
                Keep(product.Variants[1]),
                new VariantPayload(null, "Olive", null, 1900m, 1, true)
            ]));

        var olive = updated.Variants.Single(v => v.Colour == "Olive");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        (await db.StockLevels.SingleOrDefaultAsync(s => s.VariantId == olive.Id))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task Setting_is_active_false_retires_a_variant_without_removing_it()
    {
        var product = await CreateAsync(TwoColours("Retire One"));
        var black = product.Variants[1];

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [Keep(product.Variants[0]), Keep(black, active: false)]));

        updated.Variants.Count.ShouldBe(2);
        updated.Variants.Single(v => v.Id == black.Id).IsActive.ShouldBeFalse();
    }

    /// <summary>
    /// "Missing means delete" is how this pattern destroys data the first time a bug drops a
    /// row from the array. Omission must be inert; only an explicit flag retires anything.
    /// </summary>
    [Fact]
    public async Task A_variant_omitted_from_the_payload_is_left_untouched()
    {
        var product = await CreateAsync(TwoColours("Omission Test"));
        var black = product.Variants[1];

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null, [Keep(product.Variants[0])]));

        updated.Variants.Count.ShouldBe(2);
        var untouched = updated.Variants.Single(v => v.Id == black.Id);
        untouched.IsActive.ShouldBeTrue();
        untouched.SellingPrice.ShouldBe(black.SellingPrice);
        untouched.Sku.ShouldBe(black.Sku);
    }

    [Fact]
    public async Task A_retired_colour_can_be_reintroduced()
    {
        var product = await CreateAsync(TwoColours("Comeback Tan"));
        var tan = product.Variants[0];

        await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [Keep(tan, active: false), Keep(product.Variants[1])]));

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [new VariantPayload(null, "Tan", null, 2100m, 1, true)]));

        updated.Variants.Count(v => v.Colour == "Tan").ShouldBe(2);
        updated.Variants.Count(v => v.Colour == "Tan" && v.IsActive).ShouldBe(1);
    }

    [Fact]
    public async Task A_variant_belonging_to_another_product_is_rejected()
    {
        var mine = await CreateAsync(TwoColours("Mine"));
        var theirs = await CreateAsync(TwoColours("Theirs"));

        var response = await _client.PutAsJsonAsync($"/api/v1/products/{mine.Id}",
            new ProductPayload(mine.Name, null, null, [Keep(theirs.Variants[0])]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An untouched variant still occupies its colour. Without the union of unmentioned
    /// existing rows into the duplicate check, this payload is accepted and dies in the
    /// database as a 500 instead of being refused as a conflict.
    /// </summary>
    [Fact]
    public async Task A_new_variant_clashing_with_an_unmentioned_active_one_is_rejected()
    {
        var product = await CreateAsync(TwoColours("Unmentioned Clash"));

        // Mentions neither existing variant; both stay active, and Tan is still taken.
        var response = await _client.PutAsJsonAsync($"/api/v1/products/{product.Id}",
            new ProductPayload(product.Name, null, null,
                [new VariantPayload(null, "Tan", null, 2100m, 1, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Assert the message, not just the status. The DbUpdateException catch also yields a
        // 409, so a status-only assertion is satisfied by the very safety net that would catch
        // this bug's absence — the test would pass with the union deleted.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Each combination may appear once");
    }

    [Fact]
    public async Task Updating_an_unknown_product_is_not_found()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/products/999999",
            new ProductPayload("Ghost", null, null, []));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The case the retirements-first pass exists for. ux_variant_combination is a partial
    /// unique index, checked per row as the batch lands rather than at commit — so freeing
    /// "Black" and claiming it must not happen in the same write. Before the fix this passed
    /// the in-memory check and then died in the database as a 500.
    /// </summary>
    [Fact]
    public async Task A_colour_can_be_reused_in_the_same_save_that_retires_its_previous_holder()
    {
        var product = await CreateAsync(TwoColours("Handover"));
        var tan = product.Variants.Single(v => v.Colour == "Tan");
        var black = product.Variants.Single(v => v.Colour == "Black");

        var updated = await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [
                new VariantPayload(tan.Id, "Black", null, tan.SellingPrice, tan.LowStockThreshold, true),
                Keep(black, active: false)
            ]));

        updated.Variants.Single(v => v.Id == tan.Id).Colour.ShouldBe("Black");
        updated.Variants.Single(v => v.Id == tan.Id).IsActive.ShouldBeTrue();
        updated.Variants.Single(v => v.Id == black.Id).IsActive.ShouldBeFalse();
        updated.Variants.Count(v => v.IsActive && v.Colour == "Black").ShouldBe(1);
    }

    /// <summary>
    /// Two active variants swapping colours cannot be ordered to succeed against a partial
    /// unique index, and a sentinel value would be worse than the problem. So it must be
    /// refused in a way the owner can act on — a 409 that says save it in two steps — never
    /// a generic 500.
    /// </summary>
    [Fact]
    public async Task Swapping_two_active_colours_in_one_save_is_refused_with_a_conflict()
    {
        var product = await CreateAsync(TwoColours("Swap Me"));
        var tan = product.Variants.Single(v => v.Colour == "Tan");
        var black = product.Variants.Single(v => v.Colour == "Black");

        var response = await _client.PutAsJsonAsync($"/api/v1/products/{product.Id}",
            new ProductPayload(product.Name, null, null,
            [
                new VariantPayload(tan.Id, "Black", null, tan.SellingPrice, tan.LowStockThreshold, true),
                new VariantPayload(black.Id, "Tan", null, black.SellingPrice, black.LowStockThreshold, true)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("two steps");
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("SlingKing");
        body.ShouldNotContain("   at ");

        // The refusal must leave both variants exactly as they were.
        var after = await _client.GetFromJsonAsync<ProductView>($"/api/v1/products/{product.Id}");
        after!.Variants.Single(v => v.Id == tan.Id).Colour.ShouldBe("Tan");
        after.Variants.Single(v => v.Id == black.Id).Colour.ShouldBe("Black");
    }

    [Fact]
    public async Task Deactivating_hides_a_product_from_the_default_list()
    {
        var marker = $"Retired{Guid.NewGuid():N}"[..18];
        var product = await CreateAsync(TwoColours(marker));

        var response = await _client.PostAsync(
            $"/api/v1/products/{product.Id}/deactivate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync($"?search={marker}")).ShouldBeEmpty();
        (await ListAsync($"?search={marker}&includeInactive=true")).Count.ShouldBe(1);
    }

    /// <summary>
    /// Deactivating the product must not touch variant rows, so reactivating restores exactly
    /// the states they had rather than reviving ones retired individually.
    /// </summary>
    [Fact]
    public async Task Deactivating_a_product_leaves_its_variant_states_alone()
    {
        var product = await CreateAsync(TwoColours("States Preserved"));

        await UpdateAsync(product.Id, new ProductPayload(
            product.Name, null, null,
            [Keep(product.Variants[0]), Keep(product.Variants[1], active: false)]));

        await _client.PostAsync($"/api/v1/products/{product.Id}/deactivate", content: null);

        var fetched = await _client.GetFromJsonAsync<ProductView>($"/api/v1/products/{product.Id}");

        fetched!.IsActive.ShouldBeFalse();
        fetched.Variants.Single(v => v.Id == product.Variants[0].Id).IsActive.ShouldBeTrue();
        fetched.Variants.Single(v => v.Id == product.Variants[1].Id).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Deactivating_keeps_the_row()
    {
        var product = await CreateAsync(TwoColours("Still Here"));

        await _client.PostAsync($"/api/v1/products/{product.Id}/deactivate", content: null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        (await db.Products.AnyAsync(p => p.Id == product.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivating_an_unknown_product_is_not_found()
    {
        (await _client.PostAsync("/api/v1/products/999999/deactivate", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
