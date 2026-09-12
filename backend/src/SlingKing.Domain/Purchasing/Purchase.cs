using SlingKing.Domain.Common;

namespace SlingKing.Domain.Purchasing;

public class Purchase
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. PUR-000014.</summary>
    public string Number { get; set; } = null!;

    public int SupplierId { get; set; }
    public DateTimeOffset PurchaseDate { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    /// <summary>
    /// What the purchase cost in total. Null when ANY line has an unknown cost — never a
    /// partial sum of the lines that happen to be costed, because a total that omits a line
    /// understates what was paid while still looking like a figure you can trust.
    /// </summary>
    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
