using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Payments;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Direction).HasConversion<short>();
        b.Property(x => x.Method).HasConversion<short>();
        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.Reference).HasMaxLength(128);
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_payments_order");
        b.ToTable(t => t.HasCheckConstraint(
            "ck_payment_amount_positive", "amount > 0"));
    }
}

public class ReturnConfiguration : IEntityTypeConfiguration<Return>
{
    public void Configure(EntityTypeBuilder<Return> b)
    {
        b.ToTable("returns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.RefundAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.RestockedValue).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_returns_number");
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_returns_order");
    }
}

public class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> b)
    {
        b.ToTable("return_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Condition).HasConversion<short>();
        b.Property(x => x.CogsReversed).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasOne(x => x.Return).WithMany(r => r.Items)
            .HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_return_item_quantity_positive", "quantity > 0"));
    }
}
