using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Inventory;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
    }
}

public class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> b)
    {
        b.ToTable("stock_levels");
        b.HasKey(x => new { x.VariantId, x.LocationId });

        b.Property(x => x.PhysicalQty).HasDefaultValue(0);
        b.Property(x => x.ReservedQty).HasDefaultValue(0);

        // Generated column: there is no code path that can compute this differently.
        b.Property(x => x.AvailableQty)
            .HasComputedColumnSql("physical_qty - reserved_qty", stored: true);

        // The negative-stock guarantee. Any bug that tries to break these aborts
        // its whole transaction at the database rather than writing a bad number.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_physical_non_negative", "physical_qty >= 0");
            t.HasCheckConstraint("ck_reserved_non_negative", "reserved_qty >= 0");
            t.HasCheckConstraint("ck_reserved_within_physical", "reserved_qty <= physical_qty");
        });
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("inventory_transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<short>();
        b.Property(x => x.ReferenceType).HasConversion<short?>();
        b.Property(x => x.PhysicalDelta).HasDefaultValue(0);
        b.Property(x => x.ReservedDelta).HasDefaultValue(0);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)");
        b.Property(x => x.TotalCost).HasColumnType("numeric(18,2)");
        b.Property(x => x.Reason).HasMaxLength(256);

        b.HasIndex(x => new { x.VariantId, x.LocationId, x.OccurredAt })
            .HasDatabaseName("ix_inventory_transactions_variant_time");
    }
}

public class InventoryCostLayerConfiguration : IEntityTypeConfiguration<InventoryCostLayer>
{
    public void Configure(EntityTypeBuilder<InventoryCostLayer> b)
    {
        b.ToTable("inventory_cost_layers");
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceType).HasConversion<short>();

        // Optional by design. Null means unknown, never zero.
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);

        // Partial index: the FIFO consumer only ever scans open layers.
        b.HasIndex(x => new { x.VariantId, x.LocationId, x.ReceivedAt, x.Id })
            .HasDatabaseName("ix_layers_fifo")
            .HasFilter("remaining_qty > 0");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_layer_original_positive", "original_qty > 0");
            t.HasCheckConstraint("ck_layer_remaining_non_negative", "remaining_qty >= 0");
            t.HasCheckConstraint("ck_remaining_within_original", "remaining_qty <= original_qty");
            t.HasCheckConstraint("ck_layer_cost_non_negative", "unit_cost >= 0");
        });
    }
}

public class InventoryCostConsumptionConfiguration : IEntityTypeConfiguration<InventoryCostConsumption>
{
    public void Configure(EntityTypeBuilder<InventoryCostConsumption> b)
    {
        b.ToTable("inventory_cost_consumptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.ReversedQty).HasDefaultValue(0);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);
        b.Property(x => x.TotalCost).HasColumnType("numeric(18,2)").IsRequired(false);

        b.HasOne(x => x.CostLayer).WithMany()
            .HasForeignKey(x => x.CostLayerId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.OrderItemId).HasDatabaseName("ix_consumptions_order_item");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_consumption_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_consumption_reversed_non_negative", "reversed_qty >= 0");
            t.HasCheckConstraint("ck_reversed_within_quantity", "reversed_qty <= quantity");
        });
    }
}
