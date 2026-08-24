using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

[Collection("database")]
public class SchemaConstraintTests
{
    private readonly TestSchemaFixture _fixture;

    public SchemaConstraintTests(TestSchemaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A test that merely proves "something threw" is close to worthless: the write could
    /// have failed for an unrelated reason. Every rejection is pinned to SQLSTATE 23514
    /// (check_violation) and to the exact constraint that is supposed to have fired.
    /// </summary>
    private static void ShouldBeCheckViolationOf(DbUpdateException error, string constraintName)
    {
        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgres.ConstraintName.ShouldBe(constraintName);
    }

    private async Task<(long VariantId, int LocationId)> SeedVariantAsync()
    {
        var db = _fixture.Db;
        var now = DateTimeOffset.UtcNow;

        var product = new Product
        {
            Code = $"BAG-{Random.Shared.Next(100000, 999999)}",
            Name = "Test tote",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = product.Code + "-V01",
            SellingPrice = 1200m,
            LowStockThreshold = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProductVariants.Add(variant);

        var location = new Location { Name = "Home", IsDefault = true };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        return (variant.Id, location.Id);
    }

    [Fact]
    public async Task Available_quantity_is_computed_by_the_database()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = 20,
            ReservedQty = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var stored = await db.StockLevels.AsNoTracking()
            .SingleAsync(s => s.VariantId == variantId && s.LocationId == locationId);

        stored.AvailableQty.ShouldBe(15);
    }

    [Fact]
    public async Task Physical_stock_cannot_go_negative()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = -1,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ShouldBeCheckViolationOf(error, "ck_physical_non_negative");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Reserved_stock_cannot_exceed_physical_stock()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = 3,
            ReservedQty = 4,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ShouldBeCheckViolationOf(error, "ck_reserved_within_physical");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_cost_layer_may_be_saved_with_no_cost_at_all()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.OpeningStock,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 10,
            RemainingQty = 10,
            UnitCost = null,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_cost_layer_may_not_be_saved_with_a_negative_cost()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.Purchase,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 10,
            RemainingQty = 10,
            UnitCost = -1m,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ShouldBeCheckViolationOf(error, "ck_layer_cost_non_negative");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Remaining_quantity_can_never_exceed_the_original()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.Purchase,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 5,
            RemainingQty = 6,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ShouldBeCheckViolationOf(error, "ck_remaining_within_original");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Every_expected_table_exists()
    {
        var count = await _fixture.Db.Database
            .SqlQuery<int>($@"
                select count(*)::int as ""Value""
                  from information_schema.tables
                 where table_schema = 'test' and table_type = 'BASE TABLE'")
            .SingleAsync();

        // 27 domain tables plus EF's own __ef_migrations_history
        count.ShouldBe(28);
    }
}
