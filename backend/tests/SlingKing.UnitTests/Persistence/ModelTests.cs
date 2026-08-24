using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

/// <summary>
/// Model-shape assertions. These need no database — EF builds the model in memory,
/// so they run in milliseconds and catch a broken mapping before a migration is generated.
/// </summary>
public class ModelTests
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
    public void The_model_maps_exactly_twenty_seven_tables()
    {
        using var db = BuildContext();

        db.Model.GetEntityTypes().Count().ShouldBe(27);
    }

    [Fact]
    public void Table_and_column_names_are_snake_case()
    {
        using var db = BuildContext();

        var variant = db.Model.FindEntityType(typeof(SlingKing.Domain.Catalog.ProductVariant))!;

        variant.GetTableName().ShouldBe("product_variants");
        variant.FindProperty(nameof(SlingKing.Domain.Catalog.ProductVariant.LowStockThreshold))!
            .GetColumnName().ShouldBe("low_stock_threshold");
    }

    [Fact]
    public void Available_quantity_is_a_stored_generated_column()
    {
        using var db = BuildContext();

        var property = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.StockLevel))!
            .FindProperty(nameof(SlingKing.Domain.Inventory.StockLevel.AvailableQty))!;

        property.GetComputedColumnSql().ShouldBe("physical_qty - reserved_qty");
        property.GetIsStored().ShouldBe(true);
    }

    [Fact]
    public void Stock_levels_carry_the_three_negative_stock_guards()
    {
        using var db = BuildContext();

        // Check constraints are migrations-only metadata and are trimmed from the
        // read-optimized runtime model in this EF Core version, so they must be read
        // from the design-time model instead of db.Model.
        var names = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.StockLevel))!
            .GetCheckConstraints()
            .Select(c => c.Name)
            .ToArray();

        names.ShouldContain("ck_physical_non_negative");
        names.ShouldContain("ck_reserved_non_negative");
        names.ShouldContain("ck_reserved_within_physical");
    }

    [Fact]
    public void Cost_layer_unit_cost_is_nullable_and_four_decimal_places()
    {
        using var db = BuildContext();

        var property = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.InventoryCostLayer))!
            .FindProperty(nameof(SlingKing.Domain.Inventory.InventoryCostLayer.UnitCost))!;

        property.IsNullable.ShouldBeTrue();
        property.GetColumnType().ShouldBe("numeric(18,4)");
    }

    [Fact]
    public void The_fifo_index_only_covers_open_layers()
    {
        using var db = BuildContext();

        var index = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.InventoryCostLayer))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_layers_fifo");

        index.GetFilter().ShouldBe("remaining_qty > 0");
    }

    [Fact]
    public void Money_columns_use_two_decimal_places()
    {
        using var db = BuildContext();

        db.Model.FindEntityType(typeof(SlingKing.Domain.Sales.Order))!
            .FindProperty(nameof(SlingKing.Domain.Sales.Order.TotalAmount))!
            .GetColumnType().ShouldBe("numeric(18,2)");
    }
}
