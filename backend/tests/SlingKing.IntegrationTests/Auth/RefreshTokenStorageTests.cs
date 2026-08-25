using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using SlingKing.Domain.Platform;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Auth;

/// <summary>
/// The refresh-token contract is a storage property, so it is proved against the real
/// database rather than in memory: the raw token must not survive anywhere in the row,
/// and a rotated token must stop working the moment it is exchanged.
/// The endpoints that drive this live in Task 13; what is proved here is that the
/// primitives and the schema make the one-time-use rule expressible at all.
/// </summary>
[Collection("database")]
public class RefreshTokenStorageTests
{
    private readonly TestSchemaFixture _fixture;
    private readonly JwtTokenService _tokens;
    private readonly JwtOptions _options = new()
    {
        Secret = "a-test-signing-key-of-at-least-32-bytes-length",
        Issuer = "slingking-api",
        Audience = "slingking-web",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14
    };

    public RefreshTokenStorageTests(TestSchemaFixture fixture)
    {
        _fixture = fixture;
        _tokens = new JwtTokenService(Options.Create(_options), new SystemClock());
    }

    private async Task<long> SeedUserAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Email = $"user-{Guid.NewGuid():N}@slingking.test",
            PasswordHash = new BcryptPasswordHasher().Hash("initial-password-01"),
            DisplayName = "Test owner",
            CreatedAt = now,
            UpdatedAt = now
        };

        _fixture.Db.Users.Add(user);
        await _fixture.Db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<string> IssueAsync(long userId, TimeSpan? lifetime = null)
    {
        var (token, hash) = _tokens.CreateRefreshToken();
        var now = DateTimeOffset.UtcNow;

        _fixture.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime ?? TimeSpan.FromDays(_options.RefreshTokenDays))
        });
        await _fixture.Db.SaveChangesAsync();

        return token;
    }

    /// <summary>
    /// The exchange rule, exactly as the auth endpoint will apply it: look the presented
    /// token up by its hash, refuse it if it is unknown, already revoked or expired, and
    /// otherwise kill it before handing back its replacement. Returns null for a refusal.
    /// </summary>
    private async Task<string?> RotateAsync(string presented)
    {
        var now = DateTimeOffset.UtcNow;
        var presentedHash = _tokens.HashRefreshToken(presented);

        var stored = await _fixture.Db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            return null;
        }

        var (replacement, replacementHash) = _tokens.CreateRefreshToken();

        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = replacementHash;

        _fixture.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = replacementHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays)
        });
        await _fixture.Db.SaveChangesAsync();

        return replacement;
    }

    /// <summary>
    /// Casts the whole row to text, so this covers every column of every row — not just
    /// the one the test happens to think about. If the raw token were ever written to a
    /// "last token" audit column tomorrow, this fails.
    /// </summary>
    private async Task<int> RowsContainingAsync(string needle) =>
        // Runs on the context's own connection, so it neither invents credentials nor
        // spends one of the session pooler's fifteen client slots.
        await _fixture.Db.Database
            .SqlQueryRaw<int>(
                "select count(*)::int as \"Value\" from refresh_tokens rt " +
                "where rt::text like '%' || {0} || '%'",
                needle)
            .SingleAsync();

    [Fact]
    public async Task The_raw_refresh_token_appears_nowhere_in_the_database()
    {
        var userId = await SeedUserAsync();
        var raw = await IssueAsync(userId);

        // The scan itself has to be capable of finding something, or it proves nothing.
        (await RowsContainingAsync(_tokens.HashRefreshToken(raw))).ShouldBe(1);

        (await RowsContainingAsync(raw)).ShouldBe(0);
    }

    [Fact]
    public async Task What_is_stored_is_the_sha256_of_the_token_and_nothing_else()
    {
        var userId = await SeedUserAsync();
        var raw = await IssueAsync(userId);

        var stored = await _fixture.Db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == _tokens.HashRefreshToken(raw));

        stored.TokenHash.ShouldNotBe(raw);
        stored.TokenHash.ShouldBe(_tokens.HashRefreshToken(raw));
        stored.TokenHash.Length.ShouldBe(64);
        stored.TokenHash.ShouldAllBe(c => "0123456789abcdef".Contains(c));
    }

    [Fact]
    public async Task Exchanging_a_refresh_token_kills_the_one_that_was_presented()
    {
        var userId = await SeedUserAsync();
        var original = await IssueAsync(userId);

        var replacement = await RotateAsync(original);
        replacement.ShouldNotBeNull();
        replacement.ShouldNotBe(original);

        // The whole point of rotation: the presented token is spent, not merely superseded.
        (await RotateAsync(original)).ShouldBeNull();

        // ...and the replacement is the only thing that still works.
        (await RotateAsync(replacement!)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_revoked_token_records_what_replaced_it_and_stays_revoked()
    {
        var userId = await SeedUserAsync();
        var original = await IssueAsync(userId);
        var originalHash = _tokens.HashRefreshToken(original);

        var replacement = await RotateAsync(original);

        var spent = await _fixture.Db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == originalHash);

        spent.RevokedAt.ShouldNotBeNull();
        spent.ReplacedByTokenHash.ShouldBe(_tokens.HashRefreshToken(replacement!));

        // The replacement chain is stored as hashes too — no raw token anywhere.
        (await RowsContainingAsync(replacement!)).ShouldBe(0);
    }

    [Fact]
    public async Task An_expired_token_is_refused_even_though_it_was_never_revoked()
    {
        var userId = await SeedUserAsync();
        var stale = await IssueAsync(userId, lifetime: TimeSpan.FromSeconds(-1));

        (await RotateAsync(stale)).ShouldBeNull();
    }

    [Fact]
    public async Task A_token_that_was_never_issued_is_refused()
    {
        (await RotateAsync(_tokens.CreateRefreshToken().Token)).ShouldBeNull();
    }
}
