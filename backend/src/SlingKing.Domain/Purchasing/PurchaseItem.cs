namespace SlingKing.Domain.Purchasing;

public class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }

    /// <summary>Optional. A blank cost opens an uncosted layer on completion.</summary>
    public decimal? UnitCost { get; set; }

    public decimal? LineTotal { get; set; }

    public Purchase? Purchase { get; set; }
}
