using Microsoft.EntityFrameworkCore;
using Npgsql;
using SlingKing.Infrastructure.Configuration;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.IntegrationTests.Fixtures;

/// <summary>
/// Creates a throwaway "test" schema, migrates into it, and drops it afterwards.
/// The guard below is the only thing standing between a destructive test run and
/// the schema holding real stock, so it runs before a single connection is opened.
/// </summary>
public class TestSchemaFixture : IAsyncLifetime
{
    public const string SchemaName = "test";

    public AppDbContext Db { get; private set; } = null!;

    public string ConnectionString => _connectionString;

    private string _connectionString = null!;

    /// <summary>
    /// Throws unless the connection string pins the search path to exactly the test
    /// schema. A list such as "public,test" is rejected: the first entry is where
    /// unqualified writes would land.
    /// </summary>
    public static void AssertTargetsTestSchema(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SLINGKING_DB_TEST is not set. Integration tests will not run without it.");
        }

        var searchPath = new NpgsqlConnectionStringBuilder(connectionString).SearchPath;

        if (!string.Equals(searchPath?.Trim(), SchemaName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests refuse to run outside the '{SchemaName}' schema. " +
                $"SLINGKING_DB_TEST resolves to search path '{searchPath ?? "(none)"}'. " +
                "Point it at the test schema — never at public.");
        }
    }

    public async Task InitializeAsync()
    {
        EnvFile.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));

        _connectionString = Environment.GetEnvironmentVariable("SLINGKING_DB_TEST") ?? "";
        AssertTargetsTestSchema(_connectionString);

        await using (var admin = new NpgsqlConnection(_connectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText =
                $"drop schema if exists {SchemaName} cascade; create schema {SchemaName};";
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SchemaName);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        Db = new AppDbContext(options);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();

        await using var admin = new NpgsqlConnection(_connectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"drop schema if exists {SchemaName} cascade;";
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("database")]
public class DatabaseCollection : ICollectionFixture<TestSchemaFixture>
{
}
