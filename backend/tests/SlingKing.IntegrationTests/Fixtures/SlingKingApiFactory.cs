using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using SlingKing.Infrastructure.Configuration;

namespace SlingKing.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real API — the real Program.cs, the real pipeline, the real database — against
/// the test schema. Uses the same guard as <see cref="TestSchemaFixture"/>, so an in-process
/// API can no more reach real data than a direct test can.
/// </summary>
/// <remarks>
/// This factory deliberately does not migrate. Program.cs migrates on startup and the factory
/// boots Program, so a migrate here would be redundant — and the only way to reach a
/// DbContext from <c>ConfigureServices</c> is to build a second service provider, which gives
/// the process a second container whose singletons and connection pools are nobody's.
/// </remarks>
public class SlingKingApiFactory : WebApplicationFactory<Program>
{
    public const string SeedEmail = "api-test-owner@slingking.test";

    /// <summary>
    /// The seeded credential for the API tests. It is a value invented here for a throwaway
    /// schema and is unrelated to <c>SLINGKING_SEED_ADMIN_PASSWORD</c> in backend/.env, which
    /// this factory deliberately overrides so no real credential is ever exercised by a test.
    /// </summary>
    public const string SeedPassword = "api-test-password-01";

    /// <summary>
    /// The Supabase session pooler allows this project fifteen clients in total. The host
    /// under test gets a bounded slice of them so a leaked connection cannot starve the
    /// fixture, and so two factories in one run cannot between them exhaust the cap.
    /// </summary>
    private const int MaxPoolSize = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        EnvFile.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));

        var connection = Environment.GetEnvironmentVariable("SLINGKING_DB_TEST") ?? "";
        TestSchemaFixture.AssertTargetsTestSchema(connection);

        var runtime = new NpgsqlConnectionStringBuilder(connection)
        {
            MaxPoolSize = MaxPoolSize
        }.ConnectionString;

        // Program.cs migrates over SLINGKING_DB_MIGRATION and serves over SLINGKING_DB_RUNTIME.
        // BOTH are pinned to the guarded test-schema connection above. Overriding only the
        // runtime one would leave the host migrating the real public schema from inside a
        // test run — the exact thing TestSchemaFixture exists to make impossible.
        Environment.SetEnvironmentVariable("SLINGKING_DB_RUNTIME", runtime);
        Environment.SetEnvironmentVariable("SLINGKING_DB_MIGRATION", runtime);
        Environment.SetEnvironmentVariable("SLINGKING_SEED_ADMIN_EMAIL", SeedEmail);
        Environment.SetEnvironmentVariable("SLINGKING_SEED_ADMIN_PASSWORD", SeedPassword);

        builder.UseEnvironment("Testing");
    }
}
