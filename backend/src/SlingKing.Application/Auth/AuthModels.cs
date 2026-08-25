namespace SlingKing.Application.Auth;

/// <summary>The subset of a user that ever crosses the wire. No hash, no flags, no timestamps.</summary>
public record AuthUser(long Id, string Email, string DisplayName);

/// <summary>
/// The result of a successful login or refresh. The raw refresh token exists only here and
/// in the response — the database holds its SHA-256 and nothing else.
/// </summary>
public record AuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    AuthUser User);

/// <summary>
/// Token lifetimes. Lives in Application rather than beside <c>JwtOptions</c> because
/// Application must not reference Infrastructure; the composition root sets both from the
/// same two constants.
/// </summary>
public sealed class AuthOptions
{
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}
