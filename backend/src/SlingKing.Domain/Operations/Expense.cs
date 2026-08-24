namespace SlingKing.Domain.Operations;

public class Expense
{
    public long Id { get; set; }
    public int CategoryId { get; set; }

    /// <summary>Optional: attributing an expense to a channel makes per-channel net profit real.</summary>
    public int? ChannelId { get; set; }

    public decimal Amount { get; set; }
    public DateTimeOffset ExpenseDate { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }

    public ExpenseCategory? Category { get; set; }
}
