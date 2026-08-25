namespace SlingKing.Application.Auth;

/// <summary>
/// Every authentication decision the API can make. Failure is <c>null</c>, never an
/// exception and never a reason code: the caller has no way to tell "no such user" from
/// "wrong secret" because there is nothing in the return value that distinguishes them.
/// </summary>
public interface IAuthService
{
    Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new pair and revokes the one presented. A token that
    /// has already been exchanged, revoked or expired returns null.
    /// </summary>
    Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<AuthUser?> GetActiveUserAsync(long userId, CancellationToken cancellationToken = default);
}
