using SlingKing.Domain.Common;

namespace SlingKing.Domain.Sales;

public class Order
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. ORD-000001.</summary>
    public string Number { get; set; } = null!;

    public int ChannelId { get; set; }
    public long? CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    public DateTimeOffset OrderDate { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>Revenue and COGS are recognised here, not on OrderDate.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Derived from the payment rows. Never written by a client.</summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SalesChannel? Channel { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
