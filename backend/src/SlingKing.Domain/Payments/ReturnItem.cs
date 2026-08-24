using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

public class ReturnItem
{
    public long Id { get; set; }
    public long ReturnId { get; set; }
    public long OrderItemId { get; set; }
    public int Quantity { get; set; }
    public ReturnItemCondition Condition { get; set; } = ReturnItemCondition.Sellable;
    public decimal? CogsReversed { get; set; }

    public Return? Return { get; set; }
}
