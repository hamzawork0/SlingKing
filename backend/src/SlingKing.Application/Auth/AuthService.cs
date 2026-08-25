using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Platform;

namespace SlingKing.Application.Auth;

/// <summary>
/// Where the authentication rules actually live. The controller validates the request shape
/// and maps the result to a status code; every decision about whether a credential is good,
/// and every write to the refresh-token table, happens here.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly AuthOptions _options;

    public AuthService(
        IAppDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IClock clock,
        AuthOptions options)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _options = options;
    }

    public async Task<AuthTokens?> LoginAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        if (user is null)
        {
            // Both branches return the same null, but only one of them would pay for a
            // bcrypt round. Hashing a throwaway value here costs the unknown-email path the
            // same ~250ms as the wrong-password path, so the clock enumerates accounts no
            // better than the response body does.
            _ = _hasher.Hash(password);
            return null;
        }

        return _hasher.Verify(password, user.PasswordHash)
            ? await IssueAsync(user, cancellationToken)
            : null;
    }

    public async Task<AuthTokens?> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokens.HashRefreshToken(refreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            return null;
        }

        var user = stored.User;

        if (user is null || !user.IsActive)
        {
            return null;
        }

        // Rotation: the presented token dies here, whatever happens next. A replay finds
        // RevokedAt set and is refused above, which is the whole point of the exercise.
        stored.RevokedAt = now;

        var issued = await IssueAsync(user, cancellationToken);

        // Recorded after the fact so the chain can be walked during an incident. Only the
        // hash of the successor is kept, exactly as for the token itself.
        stored.ReplacedByTokenHash = _tokens.HashRefreshToken(issued.RefreshToken);
        await _db.SaveChangesAsync(cancellationToken);

        return issued;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = _tokens.HashRefreshToken(refreshToken);

        var stored = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<AuthUser?> GetActiveUserAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        return user is null ? null : new AuthUser(user.Id, user.Email, user.DisplayName);
    }

    private async Task<AuthTokens> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var (refreshToken, refreshHash) = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new AuthTokens(
            _tokens.CreateAccessToken(user.Id, user.Email),
            refreshToken,
            now.AddMinutes(_options.AccessTokenMinutes),
            new AuthUser(user.Id, user.Email, user.DisplayName));
    }
}
