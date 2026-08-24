using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Sales;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class SalesChannelConfiguration : IEntityTypeConfiguration<SalesChannel>
{
    public void Configure(EntityTypeBuilder<SalesChannel> b)
    {
        b.ToTable("sales_channels");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_sales_channels_code");
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.InstagramHandle).HasMaxLength(64);
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => x.Phone).HasDatabaseName("ix_customers_phone");
        b.HasIndex(x => x.InstagramHandle).HasDatabaseName("ix_customers_instagram");
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.PaymentStatus).HasConversion<short>();
        b.Property(x => x.SubtotalAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.DiscountAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.ShippingAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_orders_number");
        b.HasIndex(x => x.CompletedAt).HasDatabaseName("ix_orders_completed_at");
        b.HasOne(x => x.Channel).WithMany()
            .HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)");
        b.Property(x => x.DiscountAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.LineTotal).HasColumnType("numeric(18,2)");
        b.Property(x => x.CogsAmount).HasColumnType("numeric(18,2)").IsRequired(false);
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(256).IsRequired();
        b.Property(x => x.SkuSnapshot).HasMaxLength(48).IsRequired();
        b.HasOne(x => x.Order).WithMany(o => o.Items)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_order_item_quantity_positive", "quantity > 0"));
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("order_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasConversion<short?>();
        b.Property(x => x.ToStatus).HasConversion<short>();
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_order_status_history_order");
    }
}
