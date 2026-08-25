using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// "Internal exceptions never reach the client" is only a claim until something inside the
/// request actually blows up. This boots the real host with one collaborator rigged to
/// throw, carrying a connection string in its message, and reads what the client is handed.
/// </summary>
[Collection("database")]
public class ExceptionLeakageTests
    : IClassFixture<ExceptionLeakageTests.ExplodingApiFactory>, IAsyncLifetime
{
    private readonly ExplodingApiFactory _factory;
    private readonly HttpClient _client;

    public ExceptionLeakageTests(ExplodingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// The rigged hasher only throws once a user has been found, so this test needs the
    /// seeded user present however the "database" collection happens to be ordered.
    /// Seeding is idempotent; this restores a precondition rather than softening anything.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);

    [Fact]
    public async Task An_internal_failure_returns_a_safe_problem_document_and_leaks_nothing()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        // The rigged hasher threw, so the request genuinely failed rather than quietly
        // succeeding — without this the assertions below would pass on an empty 200.
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();

        // No stack trace.
        body.ShouldNotContain("   at ");
        body.ShouldNotContain("stackTrace");
        body.ShouldNotContain("StackTrace");
        body.ShouldNotContain("SlingKing.Api.Controllers");

        // No connection string, and nothing else the exception was carrying.
        body.ShouldNotContain(ExplodingPasswordHasher.LeakBait);
        body.ShouldNotContain("Host=");
        body.ShouldNotContain("Password=");
        body.ShouldNotContain("Username=");

        // No exception type name — neither the framework's nor ours.
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("InvalidDataException");
        body.ShouldNotContain("ExplodingPasswordHasher");

        // What it does say is generic, and carries a correlation id so the real detail can
        // be found in the log instead.
        body.ShouldContain("An unexpected error occurred.");
        body.ShouldContain("traceId");
    }

    /// <summary>
    /// Hashes for real so startup seeding still succeeds; fails only on the verify that the
    /// login request reaches.
    /// </summary>
    private sealed class ExplodingPasswordHasher : IPasswordHasher
    {
        public const string LeakBait =
            "Host=db.internal.example;Username=postgres;Password=not-a-real-secret";

        private readonly BcryptPasswordHasher _real = new();

        public string Hash(string password) => _real.Hash(password);

        public bool Verify(string password, string hash) =>
            throw new InvalidDataException("could not reach the credential store: " + LeakBait);
    }

    public sealed class ExplodingApiFactory : SlingKingApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Applied after Program.cs has registered its own services, so this replaces
            // the real hasher rather than racing it.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher, ExplodingPasswordHasher>();
            });
        }
    }
}
