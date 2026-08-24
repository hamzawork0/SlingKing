using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Inventory;
using SlingKing.Domain.Operations;
using SlingKing.Domain.Payments;
using SlingKing.Domain.Platform;
using SlingKing.Domain.Purchasing;
using SlingKing.Domain.Sales;

namespace SlingKing.Application.Common.Abstractions;

/// <summary>
/// The only database surface Application code sees. Deliberately not a repository:
/// wrapping EF Core in a near-identical abstraction buys nothing and actively fights
/// the row-level locking the FIFO consumer needs.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<AppSetting> AppSettings { get; }

    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }

    DbSet<Location> Locations { get; }
    DbSet<StockLevel> StockLevels { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<InventoryCostLayer> InventoryCostLayers { get; }
    DbSet<InventoryCostConsumption> InventoryCostConsumptions { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<Purchase> Purchases { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }

    DbSet<SalesChannel> SalesChannels { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }

    DbSet<Payment> Payments { get; }
    DbSet<Return> Returns { get; }
    DbSet<ReturnItem> ReturnItems { get; }

    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportRow> ImportRows { get; }

    /// <summary>Exposed so services can open explicit transactions and run the FIFO raw SQL.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
