namespace SlingKing.Domain.Sales;

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }

    /// <summary>Frozen at confirmation. Changing the variant's price never moves this.</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Set on delivery from the FIFO consumption. Null until then, or if uncosted.</summary>
    public decimal? CogsAmount { get; set; }

    public string ProductNameSnapshot { get; set; } = null!;
    public string SkuSnapshot { get; set; } = null!;

    public Order? Order { get; set; }
}
