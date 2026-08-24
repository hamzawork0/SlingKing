using Shouldly;
using SlingKing.Domain.Inventory;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class OptionalCostTests
{
    [Fact]
    public void A_new_cost_layer_has_an_unknown_cost_not_a_zero_cost()
    {
        var layer = new InventoryCostLayer();

        layer.UnitCost.ShouldBeNull();
    }

    [Fact]
    public void A_new_consumption_has_unknown_costs_not_zero_costs()
    {
        var consumption = new InventoryCostConsumption();

        consumption.UnitCost.ShouldBeNull();
        consumption.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void Available_quantity_is_derived_and_has_no_public_setter()
    {
        typeof(StockLevel)
            .GetProperty(nameof(StockLevel.AvailableQty))!
            .SetMethod!.IsPublic
            .ShouldBeFalse("available_qty is a generated column — application code must never write it");
    }
}
