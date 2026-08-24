namespace SlingKing.Domain.Sales;

public class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? InstagramHandle { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
