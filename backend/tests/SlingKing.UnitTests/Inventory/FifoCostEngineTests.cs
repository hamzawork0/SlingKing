using Shouldly;
using SlingKing.Application.Common;
using SlingKing.Application.Inventory;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;
using Xunit;

namespace SlingKing.UnitTests.Inventory;

public class FifoCostEngineTests
{
    /// <summary>
    /// Layers as the database would hand them over: already filtered to remaining_qty > 0
    /// and already ordered by (received_at, id). The engine trusts that order and does not
    /// re-sort — re-sorting here would hide a caller that forgot the ORDER BY.
    /// </summary>
    private static InventoryCostLayer Layer(long id, int remaining, decimal? unitCost) => new()
    {
        Id = id,
        VariantId = 1,
        LocationId = 1,
        SourceType = CostLayerSourceType.OpeningStock,
        SourceId = id,
        ReceivedAt = DateTimeOffset.UnixEpoch.AddDays(id),
        OriginalQty = remaining,
        RemainingQty = remaining,
        UnitCost = unitCost
    };

    [Fact]
    public void A_consumption_inside_one_layer_draws_only_from_it()
    {
        var plan = FifoCostEngine.Consume([Layer(1, 10, 120m)], 4, allowShortfall: false);

        plan.Draws.Count.ShouldBe(1);
        plan.Draws[0].ShouldBe(new LayerDraw(1, 4, 120m));
        plan.Drawn.ShouldBe(4);
        plan.TotalCost.ShouldBe(480m);
    }

    [Fact]
    public void A_consumption_crossing_two_layers_apportions_at_each_rate()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, 100m), Layer(2, 5, 200m)], 5, allowShortfall: false);

        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m), new LayerDraw(2, 2, 200m)]);
        plan.Drawn.ShouldBe(5);
        plan.TotalCost.ShouldBe(700m);
    }

    [Fact]
    public void A_consumption_exhausting_a_layer_exactly_stops_there()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, 100m), Layer(2, 5, 200m)], 3, allowShortfall: false);

        // The second layer must not appear at all. A zero-quantity draw would violate
        // ck_consumption_quantity_positive the moment Task 4 writes it out.
        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m)]);
        plan.TotalCost.ShouldBe(300m);
    }

    [Fact]
    public void Crossing_a_layer_of_unknown_cost_yields_no_total_rather_than_a_partial_one()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, null), Layer(2, 5, 200m)], 5, allowShortfall: false);

        plan.Drawn.ShouldBe(5);
        plan.Draws[0].UnitCost.ShouldBeNull();
        plan.Draws[1].UnitCost.ShouldBe(200m);

        // 400.00 would be the partial sum of the layer we do know. Reporting it would
        // understate the cost by however much the first three actually cost.
        plan.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void Layers_short_of_the_quantity_are_refused_when_a_shortfall_is_not_allowed()
    {
        var refusal = Should.Throw<AppException>(() =>
            FifoCostEngine.Consume([Layer(1, 3, 100m)], 10, allowShortfall: false));

        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldBe(
            "Stock and cost records disagree for this item — run a recount to correct it.");
    }

    [Fact]
    public void A_shortfall_is_permitted_when_the_caller_allows_it()
    {
        // This is the recount exemption. Without it the disagreement message names a fix
        // that hits the same wall and throws the same message.
        var plan = FifoCostEngine.Consume([Layer(1, 3, 100m)], 10, allowShortfall: true);

        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m)]);
        plan.Drawn.ShouldBe(3);
        plan.TotalCost.ShouldBe(300m);
    }

    [Fact]
    public void An_empty_layer_list_with_a_permitted_shortfall_draws_nothing()
    {
        var plan = FifoCostEngine.Consume([], 5, allowShortfall: true);

        plan.Draws.ShouldBeEmpty();
        plan.Drawn.ShouldBe(0);

        // Nothing was drawn, so nothing is known about what it cost. Zero would be a claim.
        plan.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void A_non_positive_quantity_is_a_programming_error_not_a_rejection()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            FifoCostEngine.Consume([Layer(1, 3, 100m)], 0, allowShortfall: false));
    }

    [Fact]
    public void An_added_layer_starts_full_and_carries_its_cost()
    {
        var now = DateTimeOffset.UnixEpoch;

        var layer = FifoCostEngine.AddLayer(
            variantId: 7, locationId: 1, CostLayerSourceType.OpeningStock,
            sourceId: 42, quantity: 10, unitCost: 120m, now);

        layer.VariantId.ShouldBe(7L);
        layer.OriginalQty.ShouldBe(10);
        layer.RemainingQty.ShouldBe(10);
        layer.UnitCost.ShouldBe(120m);
        layer.SourceId.ShouldBe(42L);

        // ReceivedAt is the FIFO queue position. Stamped now, never back-dated in M2b, so
        // insertion order IS queue order.
        layer.ReceivedAt.ShouldBe(now);
    }

    [Fact]
    public void An_added_layer_may_have_no_cost_at_all()
    {
        var layer = FifoCostEngine.AddLayer(
            1, 1, CostLayerSourceType.AdjustmentIn, 1, 3, unitCost: null,
            DateTimeOffset.UnixEpoch);

        layer.UnitCost.ShouldBeNull();
    }
}
