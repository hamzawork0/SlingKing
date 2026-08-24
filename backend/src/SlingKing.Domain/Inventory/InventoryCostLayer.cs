using SlingKing.Domain.Common;

namespace SlingKing.Domain.Inventory;

/// <summary>
/// One FIFO queue entry. Consumed oldest-first by (ReceivedAt, Id).
/// </summary>
public class InventoryCostLayer
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public int LocationId { get; set; }

    public CostLayerSourceType SourceType { get; set; }
    public long SourceId { get; set; }

    /// <summary>Queue position. A back-dated purchase takes its rightful place at the front.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public int OriginalQty { get; set; }
    public int RemainingQty { get; set; }

    /// <summary>Null means the cost is unknown, never that it is zero. Fillable later, once.</summary>
    public decimal? UnitCost { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
