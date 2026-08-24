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

    /// <summary>Sum of the lines that have a cost. Null when no line is costed.</summary>
    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
