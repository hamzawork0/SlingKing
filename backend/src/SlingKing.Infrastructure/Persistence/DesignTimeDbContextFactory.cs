using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SlingKing.Infrastructure.Configuration;

namespace SlingKing.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling. Always resolves the migration connection —
/// migrations need prepared statements and advisory locks, which the SESSION pooler
/// on 5432 supports and the transaction pooler on 6543 does not.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"));
        EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

        var connection = Environment.GetEnvironmentVariable("SLINGKING_DB_MIGRATION")
                         ?? "Host=localhost;Database=slingking_designtime;Username=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
