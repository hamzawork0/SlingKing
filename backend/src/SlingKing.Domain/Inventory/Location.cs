namespace SlingKing.Domain.Inventory;

/// <summary>
/// Present from day one and hidden in the UI while there is exactly one. Carrying the
/// column now costs nothing; adding it to a populated ledger later would be a migration
/// across every inventory row.
/// </summary>
public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
