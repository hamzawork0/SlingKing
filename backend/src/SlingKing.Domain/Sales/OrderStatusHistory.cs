using SlingKing.Domain.Common;

namespace SlingKing.Domain.Sales;

public class OrderStatusHistory
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public long? ChangedBy { get; set; }
    public string? Notes { get; set; }
}
