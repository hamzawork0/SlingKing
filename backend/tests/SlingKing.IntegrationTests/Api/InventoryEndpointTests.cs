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

    /// <summary>Opening stock straight in, so a test can set up a starting position.</summary>
    private async Task OpenAsync(long variantId, int quantity, decimal? unitCost)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", quantity, unitCost, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Damage_lowers_the_level_and_consumes_the_oldest_layer_first()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, 100m);
        await OpenAsync(variantId, 5, 200m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 5, null, "Water damaged in transit"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();
        movement!.PhysicalDelta.ShouldBe(-5);

        // 3 at 100 then 2 at 200. A last-in-first-out walk would say 1000.
        movement.TotalCost.ShouldBe(700m);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(3);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync());

        layers.Select(l => l.RemainingQty).ShouldBe([0, 3]);

        var consumptions = await QueryAsync(db => db.InventoryCostConsumptions
            .Where(c => c.InventoryTransactionId == movement.Id)
            .OrderBy(c => c.Id)
            .ToListAsync());

        consumptions.Select(c => c.Quantity).ShouldBe([3, 2]);
        consumptions.Select(c => c.UnitCost).ShouldBe([100m, 200m]);

        // Copied at consumption time, never joined from the layer, so repricing later
        // cannot rewrite what this outflow cost.
        consumptions.Select(c => c.TotalCost).ShouldBe([300m, 400m]);
    }

    [Fact]
    public async Task An_outflow_crossing_a_layer_of_unknown_cost_reports_no_total()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, null);
        await OpenAsync(variantId, 5, 200m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Loss", 5, null, "Left in a rickshaw"));

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();

        // 400.00 is the partial sum of the half we know about. Reporting it would
        // understate the cost by whatever the first three actually cost.
        movement!.TotalCost.ShouldBeNull();
    }

    [Fact]
    public async Task An_outflow_larger_than_the_stock_is_refused_and_writes_nothing()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var before = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 99, null, "Fire"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString()
            .ShouldBe("Only 6 in stock. Record a recount first if that is wrong.");

        var after = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));
        after.ShouldBe(before);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(6);
    }

    [Fact]
    public async Task An_outflow_without_a_reason_is_refused()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        foreach (var reason in new string?[] { null, "", "   " })
        {
            var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
                new MovementRequest(variantId, "PersonalUse", 1, null, reason));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"reason {reason ?? "null"}");
        }
    }

    [Fact]
    public async Task A_cost_may_not_be_attached_to_an_outflow()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 1, 50m, "Torn strap"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("A cost can only be set on stock coming in.");
    }

    [Fact]
    public async Task A_ledger_row_never_changes_once_written()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        var original = await QueryAsync(db => db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.VariantId == variantId)
            .OrderBy(t => t.Id)
            .FirstAsync());

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 2, null, "Scuffed"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Loss", 1, null, "Miscounted at the courier"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "PersonalUse", 1, null, "Gifted to my sister"));

        var reread = await QueryAsync(db => db.InventoryTransactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == original.Id));

        // Route enumeration cannot prove this: a POST updates a row as easily as a PUT does.
        // Observation is the only proof that the ledger is genuinely append-only.
        reread.PhysicalDelta.ShouldBe(original.PhysicalDelta);
        reread.ReservedDelta.ShouldBe(original.ReservedDelta);
        reread.UnitCost.ShouldBe(original.UnitCost);
        reread.TotalCost.ShouldBe(original.TotalCost);
        reread.Type.ShouldBe(original.Type);
        reread.Reason.ShouldBe(original.Reason);
        reread.OccurredAt.ShouldBe(original.OccurredAt);
    }

    [Fact]
    public async Task The_ledger_and_the_stock_level_agree_after_a_run_of_movements()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 4, null, "Water damaged in transit"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "PersonalUse", 1, null, "Gifted to my sister"));

        var summed = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantId)
            .SumAsync(t => t.PhysicalDelta));

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));

        summed.ShouldBe(level.PhysicalQty);

        var openLayers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId && l.RemainingQty > 0)
            .SumAsync(l => l.RemainingQty));

        openLayers.ShouldBe(level.PhysicalQty);
    }

    private record RecountRequest(int CountedQuantity);

    private record RecountResult(
        int Previous, int Counted, int Difference, MovementView? Movement);

    [Fact]
    public async Task Counting_more_than_the_system_says_adjusts_up_at_an_unknown_cost()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(9));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Previous.ShouldBe(6);
        result.Counted.ShouldBe(9);
        result.Difference.ShouldBe(3);
        result.Movement!.Type.ShouldBe("Adjustment");
        result.Movement.PhysicalDelta.ShouldBe(3);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(9);

        var newest = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderByDescending(l => l.Id)
            .FirstAsync());

        newest.OriginalQty.ShouldBe(3);

        // A recount says how many were counted and nothing about what they cost.
        // Zero here would claim three free bags.
        newest.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task Counting_fewer_consumes_the_difference()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(4));

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Difference.ShouldBe(-2);
        result.Movement!.PhysicalDelta.ShouldBe(-2);
        result.Movement.TotalCost.ShouldBe(200m);

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantId));
        layer.RemainingQty.ShouldBe(4);
    }

    [Fact]
    public async Task Counting_exactly_what_the_system_says_writes_nothing()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var before = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(6));

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Difference.ShouldBe(0);
        result.Movement.ShouldBeNull();

        var after = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));
        after.ShouldBe(before);
    }

    [Fact]
    public async Task A_negative_count_is_refused()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(-1));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recounting_an_unknown_variant_is_not_found()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/inventory/999999999/recount", new RecountRequest(1));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task An_ordinary_outflow_refuses_when_layers_run_short_but_a_recount_does_not()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 12, 100m);

        // Force the divergence the guard exists for: stock says 12, layers hold 8. Doing it
        // in SQL rather than through the API is the point — no endpoint can produce this
        // state, which is why the guard needs a test that manufactures it.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var layer = await db.InventoryCostLayers.SingleAsync(l => l.VariantId == variantId);
            layer.RemainingQty = 8;
            await db.SaveChangesAsync();
        }

        var refused = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 10, null, "Flooded"));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await refused.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe(
            "Stock and cost records disagree for this item — run a recount to correct it.");

        // The same shortfall, through the recount, must succeed. If it did not, the message
        // above would name a fix that hits this same wall and returns this same message.
        var recount = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(2));

        recount.StatusCode.ShouldBe(HttpStatusCode.OK);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(2);
    }

    private record StockRow(
        long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int PhysicalQty, int LowStockThreshold);

    private record StockItemView(
        long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int PhysicalQty, int LowStockThreshold, decimal? StockValue,
        List<MovementView> Movements);

    [Fact]
    public async Task The_list_shows_one_row_per_variant_with_its_level()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 7, 100m);

        var rows = await _client.GetFromJsonAsync<List<StockRow>>("/api/v1/inventory");

        var row = rows!.Single(r => r.VariantId == variantId);
        row.PhysicalQty.ShouldBe(7);
        row.Colour.ShouldBe("Tan");
        row.Size.ShouldBe("Standard");
        row.LowStockThreshold.ShouldBe(5);
        row.Sku.ShouldEndWith("-V01");
    }

    [Fact]
    public async Task The_list_can_be_searched_by_sku()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 1, null);

        var all = await _client.GetFromJsonAsync<List<StockRow>>("/api/v1/inventory");
        var sku = all!.Single(r => r.VariantId == variantId).Sku;

        var found = await _client.GetFromJsonAsync<List<StockRow>>(
            $"/api/v1/inventory?search={sku}");

        found!.Count.ShouldBe(1);
        found[0].VariantId.ShouldBe(variantId);
    }

    [Fact]
    public async Task Low_only_shows_a_variant_at_or_under_its_threshold()
    {
        var variantId = await NewVariantAsync();

        // Threshold is 5. Four is low; six is not.
        await OpenAsync(variantId, 4, null);

        var low = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");
        low!.ShouldContain(r => r.VariantId == variantId);

        await OpenAsync(variantId, 2, null);

        var notLow = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");
        notLow!.ShouldNotContain(r => r.VariantId == variantId);
    }

    [Fact]
    public async Task Low_only_ignores_a_variant_with_no_threshold_set()
    {
        // A threshold of zero would otherwise flag every empty variant forever, which makes
        // the filter useless on a catalogue that is still being entered.
        var created = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"NoThresh {Guid.NewGuid():N}"[..16], null, null,
            [new VariantRequest(null, "Black", null, 4500m, 0, true)]));

        var product = await created.Content.ReadFromJsonAsync<ProductView>();
        var variantId = product!.Variants.Single().Id;

        var low = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");

        low!.ShouldNotContain(r => r.VariantId == variantId);
    }

    [Fact]
    public async Task The_item_returns_its_history_newest_first_and_what_it_is_worth()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 4, null, "Water damaged in transit"));

        var item = await _client.GetFromJsonAsync<StockItemView>(
            $"/api/v1/inventory/{variantId}");

        item!.PhysicalQty.ShouldBe(6);
        item.Movements.Count.ShouldBe(2);
        item.Movements[0].Type.ShouldBe("Damage");
        item.Movements[0].Reason.ShouldBe("Water damaged in transit");
        item.Movements[1].Type.ShouldBe("OpeningStock");

        // Six left in a layer bought at 120.
        item.StockValue.ShouldBe(720m);
    }

    [Fact]
    public async Task An_item_holding_stock_of_unknown_cost_is_worth_an_unknown_amount()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, null);
        await OpenAsync(variantId, 5, 200m);

        var item = await _client.GetFromJsonAsync<StockItemView>(
            $"/api/v1/inventory/{variantId}");

        // 1000.00 is what the layer we know about is worth. Printing it as the total would
        // understate the stock by whatever the other three cost.
        item!.StockValue.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        var response = await _client.GetAsync("/api/v1/inventory/999999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_the_list_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/inventory")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }
}
