namespace SlingKing.Domain.Catalog;

public class Product
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. BAG-000001. Never user-editable.</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;
    public int? CategoryId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Category? Category { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
