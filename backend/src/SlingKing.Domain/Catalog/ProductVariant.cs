namespace SlingKing.Domain.Catalog;

public class ProductVariant
{
    public long Id { get; set; }
    public long ProductId { get; set; }

    /// <summary>Auto-generated from the product code, e.g. BAG-000001-V01.</summary>
    public string Sku { get; set; } = null!;

    public string? Colour { get; set; }
    public string? Size { get; set; }

    /// <summary>Current list price. Orders snapshot their own price and never read this again.</summary>
    public decimal SellingPrice { get; set; }

    public int LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Product? Product { get; set; }
}
