using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

/// <summary>
/// Refunds are rows with Direction = Refund rather than negative amounts, so that
/// "how much came in" and "how much went back" are separately answerable.
/// </summary>
public class Payment
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public PaymentDirection Direction { get; set; } = PaymentDirection.In;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}
