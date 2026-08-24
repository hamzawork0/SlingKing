namespace SlingKing.Domain.Inventory;

/// <summary>
/// Which layers a given outflow ate, and at what rate. Cost is copied here rather than
/// joined from the layer so that editing a price later cannot rewrite historical profit —
/// the single exception being a backfill, which may only ever fill a null.
/// </summary>
public class InventoryCostConsumption
{
    public long Id { get; set; }
    public long CostLayerId { get; set; }
    public long InventoryTransactionId { get; set; }
    public long? OrderItemId { get; set; }

    public int Quantity { get; set; }
    public int ReversedQty { get; set; }

    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }

    public DateTimeOffset ConsumedAt { get; set; }

    public InventoryCostLayer? CostLayer { get; set; }
}
