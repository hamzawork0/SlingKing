using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using Xunit;

namespace SlingKing.UnitTests.Auth;

/// <summary>
/// Reading a token proves what it says; only validating it proves it can be trusted.
/// These are the tokens an attacker actually presents: re-written payloads, tokens
/// signed with a key we do not hold, unsigned tokens, and tokens that have expired.
/// The parameters below mirror the ones the API host installs.
/// </summary>
public class JwtTokenTamperingTests
{
    private const string Secret = "a-test-signing-key-of-at-least-32-bytes-length";
    private const string OtherSecret = "an-entirely-different-key-of-at-least-32-bytes";

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private static JwtTokenService BuildService(string secret = Secret, IClock? clock = null) =>
        new(
            Options.Create(new JwtOptions
            {
                Secret = secret,
                Issuer = "slingking-api",
                Audience = "slingking-web",
                AccessTokenMinutes = 30,
                RefreshTokenDays = 14
            }),
            clock ?? new SystemClock());

    private static TokenValidationParameters Parameters(string secret = Secret) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        ValidIssuer = "slingking-api",
        ValidAudience = "slingking-web",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    private static void Validate(string token, string secret = Secret) =>
        new JwtSecurityTokenHandler().ValidateToken(token, Parameters(secret), out _);

    private static string Base64UrlEncode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    [Fact]
    public void An_untouched_token_passes_full_validation()
    {
        // The control: without this, every rejection below could be rejecting a token
        // that was never acceptable in the first place.
        Should.NotThrow(() => Validate(BuildService().CreateAccessToken(42, "owner@slingking.test")));
    }

    [Fact]
    public void A_token_whose_payload_was_rewritten_is_rejected()
    {
        var parts = BuildService().CreateAccessToken(42, "owner@slingking.test").Split('.');

        var forgedPayload = Base64UrlDecode(parts[1])
            .Replace("\"sub\":\"42\"", "\"sub\":\"1\"");
        forgedPayload.ShouldContain("\"sub\":\"1\"");   // the edit actually landed

        var forged = $"{parts[0]}.{Base64UrlEncode(forgedPayload)}.{parts[2]}";

        // It still parses — and still claims to be user 1. Only the signature says otherwise.
        new JwtSecurityTokenHandler().ReadJwtToken(forged).Subject.ShouldBe("1");

        Should.Throw<SecurityTokenInvalidSignatureException>(() => Validate(forged));
    }

    [Fact]
    public void A_token_signed_with_a_key_we_do_not_hold_is_rejected()
    {
        var forged = BuildService(OtherSecret).CreateAccessToken(42, "owner@slingking.test");

        Should.Throw<SecurityTokenInvalidSignatureException>(() => Validate(forged));
    }

    [Fact]
    public void An_unsigned_alg_none_token_is_rejected()
    {
        var parts = BuildService().CreateAccessToken(42, "owner@slingking.test").Split('.');
        var stripped = $"{Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.";

        Should.Throw<SecurityTokenException>(() => Validate(stripped));
    }

    [Fact]
    public void A_token_past_its_expiry_is_rejected()
    {
        var longAgo = new FixedClock(DateTimeOffset.UtcNow.AddHours(-4));

        var stale = BuildService(clock: longAgo).CreateAccessToken(42, "owner@slingking.test");

        Should.Throw<SecurityTokenExpiredException>(() => Validate(stale));
    }

    [Fact]
    public void A_token_minted_for_another_audience_is_rejected()
    {
        var foreign = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Secret = Secret,
                Issuer = "slingking-api",
                Audience = "someone-elses-app",
                AccessTokenMinutes = 30,
                RefreshTokenDays = 14
            }),
            new SystemClock());

        Should.Throw<SecurityTokenInvalidAudienceException>(
            () => Validate(foreign.CreateAccessToken(42, "owner@slingking.test")));
    }

    [Fact]
    public void A_token_minted_by_another_issuer_is_rejected()
    {
        var foreign = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Secret = Secret,
                Issuer = "not-slingking",
                Audience = "slingking-web",
                AccessTokenMinutes = 30,
                RefreshTokenDays = 14
            }),
            new SystemClock());

        Should.Throw<SecurityTokenInvalidIssuerException>(
            () => Validate(foreign.CreateAccessToken(42, "owner@slingking.test")));
    }
}
