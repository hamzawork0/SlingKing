namespace SlingKing.Domain.Platform;

public class RefreshToken
{
    public long Id { get; set; }
    public long UserId { get; set; }

    /// <summary>SHA-256 of the token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
