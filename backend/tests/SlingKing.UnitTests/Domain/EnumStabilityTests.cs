using Shouldly;
using SlingKing.Domain.Common;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class EnumStabilityTests
{
    [Fact]
    public void Inventory_transaction_type_values_are_frozen()
    {
        ((int)InventoryTransactionType.OpeningStock).ShouldBe(1);
        ((int)InventoryTransactionType.Purchase).ShouldBe(2);
        ((int)InventoryTransactionType.Sale).ShouldBe(3);
        ((int)InventoryTransactionType.Return).ShouldBe(4);
        ((int)InventoryTransactionType.Damage).ShouldBe(5);
        ((int)InventoryTransactionType.Loss).ShouldBe(6);
        ((int)InventoryTransactionType.PersonalUse).ShouldBe(7);
        ((int)InventoryTransactionType.Adjustment).ShouldBe(8);
        ((int)InventoryTransactionType.Reservation).ShouldBe(9);
        ((int)InventoryTransactionType.ReservationRelease).ShouldBe(10);
    }

    [Fact]
    public void Order_status_values_are_frozen()
    {
        ((int)OrderStatus.Draft).ShouldBe(1);
        ((int)OrderStatus.Confirmed).ShouldBe(2);
        ((int)OrderStatus.Packed).ShouldBe(3);
        ((int)OrderStatus.Shipped).ShouldBe(4);
        ((int)OrderStatus.Delivered).ShouldBe(5);
        ((int)OrderStatus.Completed).ShouldBe(6);
        ((int)OrderStatus.Cancelled).ShouldBe(7);
        ((int)OrderStatus.Returned).ShouldBe(8);
    }

    [Fact]
    public void Payment_status_carries_the_partial_refund_state()
    {
        ((int)PaymentStatus.Pending).ShouldBe(1);
        ((int)PaymentStatus.PartiallyPaid).ShouldBe(2);
        ((int)PaymentStatus.Paid).ShouldBe(3);
        ((int)PaymentStatus.PartiallyRefunded).ShouldBe(4);
        ((int)PaymentStatus.Refunded).ShouldBe(5);
    }

    [Fact]
    public void Cost_layer_sources_are_frozen()
    {
        ((int)CostLayerSourceType.OpeningStock).ShouldBe(1);
        ((int)CostLayerSourceType.Purchase).ShouldBe(2);
        ((int)CostLayerSourceType.ReturnRestock).ShouldBe(3);
        ((int)CostLayerSourceType.AdjustmentIn).ShouldBe(4);
    }

    [Fact]
    public void No_enum_uses_zero_so_a_default_struct_is_always_invalid()
    {
        var enums = typeof(InventoryTransactionType).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "SlingKing.Domain.Common");

        foreach (var type in enums)
        {
            Enum.GetValues(type)
                .Cast<object>()
                .Select(v => Convert.ToInt32(v))
                .ShouldNotContain(0, $"{type.Name} must not define a zero value");
        }
    }
}
