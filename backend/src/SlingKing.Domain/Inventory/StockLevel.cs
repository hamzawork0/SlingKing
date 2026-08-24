namespace SlingKing.Domain.Inventory;

/// <summary>
/// Fast-read projection of the ledger. Composite key (VariantId, LocationId).
/// </summary>
public class StockLevel
{
    public long VariantId { get; set; }
    public int LocationId { get; set; }
    public int PhysicalQty { get; set; }
    public int ReservedQty { get; set; }

    /// <summary>
    /// Generated column: <c>physical_qty - reserved_qty</c>, stored. Read-only by design —
    /// there is no code path that can compute it differently from the database.
    /// </summary>
    public int AvailableQty { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
