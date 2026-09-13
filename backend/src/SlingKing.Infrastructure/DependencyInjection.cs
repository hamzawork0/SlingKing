using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SlingKing.Application.Catalog;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Application.Inventory;
using SlingKing.Application.Purchasing;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;

namespace SlingKing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string runtimeConnectionString)
    {
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(runtimeConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<INumberSequenceAllocator, NumberSequenceAllocator>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseService, PurchaseService>();

        // The seeder takes its two credentials as plain constructor arguments rather than
        // reading configuration itself, so nothing but this line knows where they come from.
        services.AddScoped(sp => new DatabaseSeeder(
            sp.GetRequiredService<IAppDbContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPasswordHasher>(),
            Environment.GetEnvironmentVariable("SLINGKING_SEED_ADMIN_EMAIL") ?? "",
            Environment.GetEnvironmentVariable("SLINGKING_SEED_ADMIN_PASSWORD") ?? ""));

        return services;
    }
}
