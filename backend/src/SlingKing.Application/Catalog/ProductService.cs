using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Inventory;

namespace SlingKing.Application.Catalog;

public sealed class ProductService : IProductService
{
    private readonly IAppDbContext _db;
    private readonly INumberSequenceAllocator _sequences;
    private readonly IClock _clock;

    public ProductService(
        IAppDbContext db, INumberSequenceAllocator sequences, IClock clock)
    {
        _db = db;
        _sequences = sequences;
        _clock = clock;
    }

    /// <summary>Blank and whitespace collapse to null, so "" and null cannot both exist.</summary>
    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RejectDuplicateCombinations(IReadOnlyList<VariantInput> variants)
    {
        var seen = new HashSet<(string, string)>();

        foreach (var variant in variants.Where(v => v.IsActive))
        {
            var key = (Normalise(variant.Colour)?.ToLowerInvariant() ?? "",
                       Normalise(variant.Size)?.ToLowerInvariant() ?? "");

            if (!seen.Add(key))
            {
                throw CatalogException.Conflict(
                    "Two variants have the same colour and size. Each combination may appear once.");
            }
        }
    }

    private static void RejectInvalidVariants(IReadOnlyList<VariantInput> variants)
    {
        foreach (var variant in variants)
        {
            if (variant.SellingPrice < 0)
            {
                throw CatalogException.Invalid("A selling price cannot be negative.");
            }

            if (variant.LowStockThreshold < 0)
            {
                throw CatalogException.Invalid("A low stock threshold cannot be negative.");
            }
        }
    }

    private async Task RequireKnownCategoryAsync(int? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        var exists = await _db.Categories
            .AnyAsync(c => c.Id == categoryId && c.IsActive, cancellationToken);

        if (!exists)
        {
            throw CatalogException.Invalid("That category does not exist.");
        }
    }

    private Task<int> DefaultLocationIdAsync(CancellationToken cancellationToken) =>
        _db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync(cancellationToken);

    public async Task<ProductView?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Code, p.Name, p.CategoryId, p.Description, p.IsActive,
                CategoryName = p.Category == null ? null : p.Category.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        // Left join onto stock_levels so a variant whose row is somehow missing reads 0
        // rather than vanishing from the response.
        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == id)
            .OrderBy(v => v.Sku)
            .Select(v => new VariantView(
                v.Id, v.Sku, v.Colour, v.Size, v.SellingPrice, v.LowStockThreshold, v.IsActive,
                _db.StockLevels.Where(s => s.VariantId == v.Id).Sum(s => (int?)s.PhysicalQty) ?? 0))
            .ToListAsync(cancellationToken);

        return new ProductView(
            product.Id, product.Code, product.Name, product.CategoryId, product.CategoryName,
            product.Description, product.IsActive,
            variants.Where(v => v.IsActive).Sum(v => v.PhysicalQty),
            variants);
    }

    public async Task<ProductView> CreateAsync(
        ProductInput input, CancellationToken cancellationToken = default)
    {
        await RequireKnownCategoryAsync(input.CategoryId, cancellationToken);

        // No variations is a legitimate answer, not a missing one. Stock lives at variant
        // level, so a product with no variant could never hold stock.
        var requested = input.Variants.Count > 0
            ? input.Variants
            : [new VariantInput(null, null, null, 0m, 0, true)];

        RejectInvalidVariants(requested);
        RejectDuplicateCombinations(requested);

        var now = _clock.UtcNow;
        var defaultLocationId = await DefaultLocationIdAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Inside the transaction on purpose: the allocator's UPDATE rolls back with
        // everything else, so a failed create returns its number to the sequence.
        var code = await _sequences.NextAsync("product", cancellationToken);

        var product = new Product
        {
            Code = code,
            Name = input.Name.Trim(),
            CategoryId = input.CategoryId,
            Description = Normalise(input.Description),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        var variants = requested.Select((variant, index) => new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"{code}-V{index + 1:00}",
            Colour = Normalise(variant.Colour),
            Size = Normalise(variant.Size),
            SellingPrice = variant.SellingPrice,
            LowStockThreshold = variant.LowStockThreshold,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _db.ProductVariants.AddRange(variants);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var variant in variants)
        {
            _db.StockLevels.Add(new StockLevel
            {
                VariantId = variant.Id,
                LocationId = defaultLocationId,
                PhysicalQty = 0,
                ReservedQty = 0,
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetAsync(product.Id, cancellationToken))!;
    }
}
