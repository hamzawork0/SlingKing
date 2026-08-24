using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Operations;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("expense_categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_expense_categories_name");
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.Description).HasMaxLength(256);
        b.HasIndex(x => x.ExpenseDate).HasDatabaseName("ix_expenses_date");
        b.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_expense_amount_positive", "amount > 0"));
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("import_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
    }
}

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> b)
    {
        b.ToTable("import_rows");
        b.HasKey(x => x.Id);
        b.Property(x => x.RawData).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.ErrorMessage).HasMaxLength(512);
        b.HasOne(x => x.Batch).WithMany(bb => bb.Rows)
            .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.BatchId).HasDatabaseName("ix_import_rows_batch");
    }
}
