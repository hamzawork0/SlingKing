namespace SlingKing.Domain.Catalog;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
