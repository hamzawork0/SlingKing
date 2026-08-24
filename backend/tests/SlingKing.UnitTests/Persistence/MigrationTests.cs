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
    public void There_is_exactly_one_migration_and_it_is_the_initial_schema()
    {
        using var db = BuildContext();

        var migrations = db.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBe(1);
        migrations[0].ShouldEndWith("InitialSchema");
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
