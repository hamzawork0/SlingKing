namespace SlingKing.Domain.Platform;

/// <summary>
/// Gap-free document numbering. Allocated with a single UPDATE … RETURNING so two
/// concurrent callers can never receive the same number.
/// </summary>
public class NumberSequence
{
    public string Key { get; set; } = null!;      // "order", "purchase", "return", "product"
    public string Prefix { get; set; } = null!;   // "ORD", "PUR", "RET", "BAG"
    public long NextValue { get; set; } = 1;
    public int Padding { get; set; } = 6;
}
