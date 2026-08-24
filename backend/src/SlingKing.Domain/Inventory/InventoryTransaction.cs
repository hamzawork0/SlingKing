using SlingKing.Domain.Common;

namespace SlingKing.Domain.Inventory;

/// <summary>
/// The append-only ledger. Both deltas on one row is what keeps reservation and
/// consumption in a single timeline: a Reservation writes (0, +5); a Sale writes (-5, -5).
/// Rows are never updated and never deleted.
/// </summary>
public class InventoryTransaction
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public int LocationId { get; set; }
    public InventoryTransactionType Type { get; set; }

    public int PhysicalDelta { get; set; }
    public int ReservedDelta { get; set; }

    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }

    public InventoryReferenceType? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
