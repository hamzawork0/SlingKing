using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(128);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Email).HasMaxLength(256);
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.ToTable("purchases");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_purchases_number");
        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> b)
    {
        b.ToTable("purchase_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);
        b.Property(x => x.LineTotal).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasOne(x => x.Purchase).WithMany(p => p.Items)
            .HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_purchase_item_quantity_positive", "quantity > 0"));
    }
}
