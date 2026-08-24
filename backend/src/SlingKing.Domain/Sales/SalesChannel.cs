namespace SlingKing.Domain.Sales;

/// <summary>
/// Channel behaviour is data, not code. Adding Meesho later is a row, not a table.
/// </summary>
public class SalesChannel
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;      // instagram, whatsapp, offline
    public string Name { get; set; } = null!;

    /// <summary>Offline sales hand the bag over immediately and never reserve.</summary>
    public bool UsesReservation { get; set; }

    /// <summary>Drives which status transitions are legal for orders on this channel.</summary>
    public bool RequiresShipping { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
