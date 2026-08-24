using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

public class Return
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. RET-000003.</summary>
    public string Number { get; set; } = null!;

    public long OrderId { get; set; }
    public ReturnStatus Status { get; set; } = ReturnStatus.Draft;
    public DateTimeOffset ReturnDate { get; set; }
    public decimal RefundAmount { get; set; }

    /// <summary>COGS credited back by the FIFO reversal. Null if the original was uncosted.</summary>
    public decimal? RestockedValue { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
}
