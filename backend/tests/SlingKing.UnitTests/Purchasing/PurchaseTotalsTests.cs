using Shouldly;
using SlingKing.Application.Common;
using SlingKing.Application.Purchasing;
using Xunit;

namespace SlingKing.UnitTests.Purchasing;

public class PurchaseTotalsTests
{
    private static PurchaseLineInput Line(long variantId, int quantity, decimal? unitCost) =>
        new(variantId, quantity, unitCost);

    [Fact]
    public void A_line_total_is_quantity_times_cost()
    {
        PurchaseTotals.LineTotal(12, 450m).ShouldBe(5400m);
    }

    [Fact]
    public void A_line_with_an_unknown_cost_has_an_unknown_total()
    {
        // Not zero. Zero would claim the bags were free.
        PurchaseTotals.LineTotal(12, null).ShouldBeNull();
    }

    [Fact]
    public void A_fully_costed_purchase_totals_its_lines()
    {
        var total = PurchaseTotals.Total([Line(1, 12, 450m), Line(2, 8, 480m)]);

        total.ShouldBe(9240m);
    }

    [Fact]
    public void One_unknown_cost_makes_the_whole_total_unknown()
    {
        var total = PurchaseTotals.Total([Line(1, 12, 450m), Line(2, 8, null)]);

        // 5400 is the sum of the line we happen to know. Reporting it would understate what
        // was actually paid by whatever the second line cost, while looking like a real
        // figure — the same defect the FIFO engine's TotalOf refuses to produce.
        total.ShouldBeNull();
    }

    [Fact]
    public void A_purchase_with_no_costs_at_all_has_no_total()
    {
        PurchaseTotals.Total([Line(1, 12, null), Line(2, 8, null)]).ShouldBeNull();
    }

    [Fact]
    public void An_empty_purchase_has_no_total()
    {
        PurchaseTotals.Total([]).ShouldBeNull();
    }

    [Fact]
    public void A_variant_appearing_twice_is_refused_by_name()
    {
        var labels = new Dictionary<long, string> { [1] = "Tan", [2] = "Black" };

        var refusal = Should.Throw<AppException>(() =>
            PurchaseTotals.RejectDuplicateVariants(
                [Line(1, 12, 450m), Line(2, 8, 480m), Line(1, 3, 450m)], labels));

        refusal.StatusCode.ShouldBe(400);
        refusal.Message.ShouldBe("Tan appears twice. Put the whole quantity on one line.");
    }

    [Fact]
    public void Distinct_variants_are_accepted()
    {
        var labels = new Dictionary<long, string> { [1] = "Tan", [2] = "Black" };

        Should.NotThrow(() => PurchaseTotals.RejectDuplicateVariants(
            [Line(1, 12, 450m), Line(2, 8, 480m)], labels));
    }

    [Fact]
    public void A_duplicate_with_no_label_still_refuses_readably()
    {
        // The label lookup is built from the variants the caller resolved. If one is missing
        // the refusal must still be a sentence, not a crash or a bare id.
        var refusal = Should.Throw<AppException>(() =>
            PurchaseTotals.RejectDuplicateVariants(
                [Line(9, 1, null), Line(9, 1, null)], new Dictionary<long, string>()));

        refusal.StatusCode.ShouldBe(400);
        refusal.Message.ShouldBe("That item appears twice. Put the whole quantity on one line.");
    }
}
