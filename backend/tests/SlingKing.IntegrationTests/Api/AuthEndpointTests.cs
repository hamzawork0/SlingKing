using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// Everything here is asserted over HTTP against the real host: the real routing, the real
/// JWT bearer handler, the real AuthController and the real database. The primitives were
/// proved in Tasks 11 and 12; what is proved here is that the endpoints actually wire them
/// together — most importantly that a refresh token really does die when it is exchanged.
/// </summary>
[Collection("database")]
public class AuthEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// The "database" collection shares one schema, and <c>SeedingTests</c> truncates the
    /// users table to make its counts exact. Re-running the seeder — which Task 12 proved
    /// idempotent row-for-row — makes these tests independent of the order xUnit picks
    /// rather than dependent on it. It restores a precondition; it softens no assertion.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record UserDto(long Id, string Email, string DisplayName);
    private record LoginResponse(
        string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, UserDto User);
    private record RefreshRequest(string RefreshToken);

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

    private async Task<LoginResponse> LoginAsSeededUserAsync()
    {
        var response = await LoginAsync(
            SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshToken));

    [Fact]
    public async Task Health_is_green_without_a_token()
    {
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correct_credentials_return_a_token_pair()
    {
        var response = await LoginAsync(
            SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.User.Email.ShouldBe(SlingKingApiFactory.SeedEmail);
        body.ExpiresAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_wrong_password_is_401_and_says_nothing_useful()
    {
        var response = await LoginAsync(SlingKingApiFactory.SeedEmail, "not-the-password");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("password");
        body.ShouldNotContain("Bcrypt");
    }

    [Fact]
    public async Task An_unknown_email_is_also_401_and_indistinguishable()
    {
        var unknown = await LoginAsync("nobody@slingking.test", "whatever");
        var wrongPassword = await LoginAsync(SlingKingApiFactory.SeedEmail, "whatever");

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Byte-for-byte identical, not merely "both are 401". A traceId or a differing
        // detail string would hand an attacker the same email oracle a 404 would.
        (await unknown.Content.ReadAsStringAsync())
            .ShouldBe(await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        (await _client.GetAsync("/api/v1/auth/me")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_current_user_when_authenticated()
    {
        var login = await LoginAsSeededUserAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UserDto>())!
            .Email.ShouldBe(SlingKingApiFactory.SeedEmail);
    }

    /// <summary>
    /// The one-time-use rule end to end. Task 11 proved the schema can express it and that
    /// only hashes are stored, but it implemented the exchange itself inside the test —
    /// rotation actually lives in the refresh endpoint. This drives the real endpoint over
    /// HTTP, and it checks both halves of the property: the spent token is refused, and the
    /// token that replaced it still works. Without the second half a refresh endpoint that
    /// was simply broken for everything would pass.
    /// </summary>
    [Fact]
    public async Task A_refresh_token_can_be_exchanged_exactly_once()
    {
        var login = await LoginAsSeededUserAsync();

        var first = await RefreshAsync(login.RefreshToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotated = (await first.Content.ReadFromJsonAsync<LoginResponse>())!;
        rotated.RefreshToken.ShouldNotBe(login.RefreshToken);

        // Replaying the same token must fail — rotation is the point.
        var replay = await RefreshAsync(login.RefreshToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the refusal must be specific to the spent token, not rotation being
        // broken outright: the replacement is still good.
        var withReplacement = await RefreshAsync(rotated.RefreshToken);
        withReplacement.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Which is itself now spent. Replay is refused at every generation, not just once.
        (await RefreshAsync(rotated.RefreshToken)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logging_out_revokes_the_refresh_token()
    {
        var login = await LoginAsSeededUserAsync();

        var logout = await _client.PostAsJsonAsync(
            "/api/v1/auth/logout", new RefreshRequest(login.RefreshToken));
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterLogout = await RefreshAsync(login.RefreshToken);
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_validation_failure_is_problem_details_not_a_stack_trace()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("", ""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("   at ");
    }
}
