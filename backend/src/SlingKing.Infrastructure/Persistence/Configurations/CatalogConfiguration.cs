using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Catalog;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasOne(x => x.Parent).WithMany()
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_products_code");
        b.HasOne(x => x.Category).WithMany(c => c.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("product_variants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(48).IsRequired();
        b.Property(x => x.Colour).HasMaxLength(64);
        b.Property(x => x.Size).HasMaxLength(64);
        b.Property(x => x.SellingPrice).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_product_variants_sku");
        b.HasOne(x => x.Product).WithMany(p => p.Variants)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_variant_price_non_negative", "selling_price >= 0"));
    }
}
