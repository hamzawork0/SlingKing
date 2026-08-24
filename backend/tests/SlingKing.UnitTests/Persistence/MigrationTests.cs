using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

public class MigrationTests
{
    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void The_migrations_are_exactly_the_two_we_expect_in_order()
    {
        using var db = BuildContext();

        var migrations = db.Database.GetMigrations().ToArray();

        // Named and ordered deliberately. A stray migration, a rename, or a
        // reordering all fail here rather than surfacing on a production deploy.
        migrations.Length.ShouldBe(2);
        migrations[0].ShouldEndWith("InitialSchema");
        migrations[1].ShouldEndWith("EnableRowLevelSecurity");
    }

    [Fact]
    public void The_model_has_no_changes_left_unmigrated()
    {
        using var db = BuildContext();

        var differ = db.GetService<IMigrationsModelDiffer>();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var source = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);

        var differences = differ.GetDifferences(
            source.GetRelationalModel(),
            db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());

        differences.ShouldBeEmpty(
            "the model has drifted from the migration — run 'dotnet ef migrations add'");
    }
}
