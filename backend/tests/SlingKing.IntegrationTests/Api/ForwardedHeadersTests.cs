using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// Behind a proxy every request arrives carrying the proxy's address, so a rate limiter
/// partitioned on the connection IP puts every visitor in one shared bucket — one person
/// mistyping a password locks everybody else out. These tests prove the limiter sees the
/// forwarded client address instead.
/// </summary>
[Collection("database")]
public class ForwardedHeadersTests : IClassFixture<ForwardedHeadersTests.ThrottledApiFactory>
{
    private readonly ThrottledApiFactory _factory;

    public ForwardedHeadersTests(ThrottledApiFactory factory) => _factory = factory;

    /// <summary>
    /// The real factory sets the login limit to 1000 so the suite cannot trip it. This one
    /// sets 2, which is the whole point of the test.
    /// </summary>
    /// <remarks>
    /// Setting the variable process-wide is safe here even though environment variables are
    /// global: Program.cs reads it once while the host is being built, so each factory's host
    /// captures whatever was set at ITS build time. Nothing re-reads it later.
    /// </remarks>
    public class ThrottledApiFactory : SlingKingApiFactory
    {
        public const int AttemptsPerMinute = 2;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            Environment.SetEnvironmentVariable(
                "SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE", AttemptsPerMinute.ToString());
        }
    }

    private record LoginRequest(string Email, string Password);

    private static HttpRequestMessage LoginFrom(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("nobody@slingking.test", "definitely-wrong"))
        };

        request.Headers.Add("X-Forwarded-For", clientIp);
        request.Headers.Add("X-Forwarded-Proto", "https");

        return request;
    }

    [Fact]
    public async Task One_client_exhausting_the_limit_does_not_lock_out_another()
    {
        using var client = _factory.CreateClient();

        // Spend the first address's whole allowance, then one more to be certain it is spent.
        for (var attempt = 0; attempt <= ThrottledApiFactory.AttemptsPerMinute; attempt++)
        {
            await client.SendAsync(LoginFrom("203.0.113.10"));
        }

        var exhausted = await client.SendAsync(LoginFrom("203.0.113.10"));
        exhausted.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // A DIFFERENT client, first attempt. Without the forwarded headers both addresses
        // collapse to the proxy's, share one bucket, and this comes back 429 — someone else's
        // typing locking this person out.
        var innocent = await client.SendAsync(LoginFrom("198.51.100.20"));

        innocent.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        innocent.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_forwarded_scheme_is_honoured()
    {
        // Render terminates TLS and forwards over http. Without X-Forwarded-Proto being
        // trusted, anything that reads Request.Scheme believes the site is plain http.
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/health")
            {
                Headers = { { "X-Forwarded-Proto", "https" }, { "X-Forwarded-For", "203.0.113.30" } }
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The two tests above go through the request pipeline, but WebApplicationFactory's
    /// TestServer leaves Connection.RemoteIpAddress null on every request, and
    /// ForwardedHeadersMiddleware only refuses forwarded headers when that address is non-null
    /// and untrusted — so a behavioural test can never catch KnownNetworks/KnownProxies being
    /// left at their default (loopback-only) values. This test pins the configuration directly
    /// instead, so removing the two Clear() calls in Program.cs fails something.
    /// </summary>
    [Fact]
    public void The_forwarded_headers_options_trust_a_non_loopback_proxy()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.KnownNetworks.ShouldBeEmpty();
        options.KnownProxies.ShouldBeEmpty();

        // Render puts three hops in X-Forwarded-For: the client, Cloudflare's egress, and its
        // own internal address. The default limit of 1 stops on that internal address, which
        // changes per connection — so the limiter partitioned every request separately and
        // never fired. This is a configuration assertion for the same reason as the two above:
        // TestServer leaves the socket address null, so no behavioural test can see the
        // difference between walking one hop and walking three.
        options.ForwardLimit.ShouldBe(3);
        options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor).ShouldBeTrue();
        options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto).ShouldBeTrue();
    }
}
