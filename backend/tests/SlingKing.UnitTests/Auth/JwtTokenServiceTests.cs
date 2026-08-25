using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using Xunit;

namespace SlingKing.UnitTests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService BuildService(IClock? clock = null)
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "a-test-signing-key-of-at-least-32-bytes-length",
            Issuer = "slingking-api",
            Audience = "slingking-web",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14
        });

        return new JwtTokenService(options, clock ?? new SystemClock());
    }

    [Fact]
    public void An_access_token_carries_the_user_id_and_email()
    {
        var token = BuildService().CreateAccessToken(42, "owner@slingking.test");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        parsed.Subject.ShouldBe("42");
        parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email)
            .Value.ShouldBe("owner@slingking.test");
        parsed.Issuer.ShouldBe("slingking-api");
        parsed.Audiences.ShouldContain("slingking-web");
    }

    [Fact]
    public void An_access_token_expires_after_the_configured_window()
    {
        var token = BuildService().CreateAccessToken(1, "a@b.test");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        (parsed.ValidTo - parsed.ValidFrom).TotalMinutes.ShouldBe(30, tolerance: 1);
    }

    [Fact]
    public void Every_access_token_has_a_unique_jti()
    {
        var service = BuildService();
        var handler = new JwtSecurityTokenHandler();

        var first = handler.ReadJwtToken(service.CreateAccessToken(1, "a@b.test"));
        var second = handler.ReadJwtToken(service.CreateAccessToken(1, "a@b.test"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void A_refresh_token_is_returned_raw_but_only_its_hash_is_meant_for_storage()
    {
        var (token, hash) = BuildService().CreateRefreshToken();

        token.Length.ShouldBeGreaterThan(40);
        hash.ShouldNotBe(token);
        hash.Length.ShouldBe(64);   // SHA-256, lowercase hex
    }

    [Fact]
    public void Hashing_a_refresh_token_is_deterministic()
    {
        var service = BuildService();
        var (token, hash) = service.CreateRefreshToken();

        service.HashRefreshToken(token).ShouldBe(hash);
    }

    [Fact]
    public void Two_refresh_tokens_are_never_the_same()
    {
        var service = BuildService();

        service.CreateRefreshToken().Token.ShouldNotBe(service.CreateRefreshToken().Token);
    }

    [Fact]
    public void A_short_secret_is_refused_at_construction_rather_than_at_first_login()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "too-short",
            Issuer = "i",
            Audience = "a",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14
        });

        Should.Throw<InvalidOperationException>(
            () => new JwtTokenService(options, new SystemClock()));
    }
}
