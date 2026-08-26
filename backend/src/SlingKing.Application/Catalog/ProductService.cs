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

        try
        {
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
                IsActive = variant.IsActive,
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
        }
        catch (DbUpdateException dbFailure)
            when (dbFailure.InnerException?.Message.Contains("ux_variant_combination") == true)
        {
            // ux_variant_combination is a partial unique index, so Postgres enforces it per
            // row as the batch lands rather than at commit. RejectDuplicateCombinations above
            // only proves the requested state is clean in memory; a concurrent create can still
            // lose the race at the database. The filter is a string match on the constraint
            // name rather than a PostgresException/SqlState check because Application must stay
            // free of any database-provider package — it references only
            // Microsoft.EntityFrameworkCore.Relational plus Domain.
            throw CatalogException.Conflict(
                "That change would give two active variants the same colour and size. " +
                "If you are swapping colours between variants, save the change in two steps.",
                dbFailure);
        }

        return (await GetAsync(product.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<ProductView>> ListAsync(
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            // Provider-agnostic on purpose. EF.Functions.ILike would drag the Npgsql provider
            // package into Application, which otherwise has no database-provider dependency at
            // all. Nothing is lost: a leading-wildcard match cannot use a plain btree index
            // either way, and a pg_trgm index would serve lower(x) LIKE '%…%' just as well.
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Code.ToLower().Contains(term) ||
                p.Variants.Any(v => v.Sku.ToLower().Contains(term)));
        }

        var ids = await query
            .OrderByDescending(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // ponytail: N+1 by construction — one GetAsync per product. Fine for a catalogue of
        // a few hundred bags on a single-user system. Replace with one projected query if
        // the list ever feels slow.
        var products = new List<ProductView>(ids.Count);

        foreach (var id in ids)
        {
            var product = await GetAsync(id, cancellationToken);

            if (product is not null)
            {
                products.Add(product);
            }
        }

        return products;
    }

    public async Task<ProductView> UpdateAsync(
        long id, ProductInput input, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw CatalogException.NotFound("That product does not exist.");

        await RequireKnownCategoryAsync(input.CategoryId, cancellationToken);
        RejectInvalidVariants(input.Variants);

        var known = product.Variants.ToDictionary(v => v.Id);

        foreach (var row in input.Variants.Where(r => r.Id is not null))
        {
            if (!known.ContainsKey(row.Id!.Value))
            {
                throw CatalogException.Invalid(
                    "A variant in the request does not belong to this product.");
            }
        }

        var withIds = input.Variants.Where(r => r.Id is not null).Select(r => r.Id!.Value).ToList();

        if (withIds.Count != withIds.Distinct().Count())
        {
            throw CatalogException.Invalid("The same variant appears more than once in the request.");
        }

        // The combination check must consider the product's whole active picture after the
        // edit, not just the rows in the payload — an untouched variant still occupies its
        // colour. Rows the payload does not mention keep whatever state they already had.
        var mentioned = input.Variants
            .Where(r => r.Id is not null)
            .Select(r => r.Id!.Value)
            .ToHashSet();

        var resulting = input.Variants
            .Concat(product.Variants
                .Where(v => !mentioned.Contains(v.Id))
                .Select(v => new VariantInput(
                    v.Id, v.Colour, v.Size, v.SellingPrice, v.LowStockThreshold, v.IsActive)))
            .ToList();

        RejectDuplicateCombinations(resulting);

        var now = _clock.UtcNow;
        var defaultLocationId = await DefaultLocationIdAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        product.Name = input.Name.Trim();
        product.CategoryId = input.CategoryId;
        product.Description = Normalise(input.Description);
        product.UpdatedAt = now;

        // Ordinals come from the count of every variant ever created for this product,
        // active or not. Variants are never removed, so the count only grows and an ordinal
        // is never reused.
        var ordinal = product.Variants.Count;
        var created = new List<ProductVariant>();

        try
        {
            // ux_variant_combination is a partial unique index, so Postgres checks it per row
            // as a batch lands rather than deferring to commit. RejectDuplicateCombinations
            // above only proves the FINAL state is clean; it says nothing about the order EF
            // sends the UPDATEs in. Applying retirements first, and saving, frees a combination
            // before anything else can claim it — that is what lets a payload rename a colour
            // onto one being retired in the same update.
            foreach (var row in input.Variants.Where(r => r.Id is not null && !r.IsActive))
            {
                var retiring = known[row.Id!.Value];
                retiring.IsActive = false;
                retiring.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);

            foreach (var row in input.Variants)
            {
                if (row.Id is not null)
                {
                    var existing = known[row.Id.Value];
                    existing.Colour = Normalise(row.Colour);
                    existing.Size = Normalise(row.Size);
                    existing.SellingPrice = row.SellingPrice;
                    existing.LowStockThreshold = row.LowStockThreshold;
                    existing.IsActive = row.IsActive;
                    existing.UpdatedAt = now;
                    continue;
                }

                ordinal++;

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = $"{product.Code}-V{ordinal:00}",
                    Colour = Normalise(row.Colour),
                    Size = Normalise(row.Size),
                    SellingPrice = row.SellingPrice,
                    LowStockThreshold = row.LowStockThreshold,
                    IsActive = row.IsActive,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _db.ProductVariants.Add(variant);
                created.Add(variant);
            }

            await _db.SaveChangesAsync(cancellationToken);

            foreach (var variant in created)
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
        }
        catch (DbUpdateException dbFailure)
            when (dbFailure.InnerException?.Message.Contains("ux_variant_combination") == true)
        {
            // ux_variant_combination is a partial unique index, so Postgres enforces it per
            // row as the batch lands rather than at commit. Two active variants swapping
            // colours in a single save therefore cannot be ordered to succeed — the honest
            // answer is to say so, not to return a generic 500. The filter is a string match on
            // the constraint name rather than a PostgresException/SqlState check because
            // Application must stay free of any database-provider package — it references only
            // Microsoft.EntityFrameworkCore.Relational plus Domain. Anything else propagates to
            // become a logged 500 instead of being mislabelled as a duplicate colour.
            throw CatalogException.Conflict(
                "That change would give two active variants the same colour and size. " +
                "If you are swapping colours between variants, save the change in two steps.",
                dbFailure);
        }

        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw CatalogException.NotFound("That product does not exist.");

        product.IsActive = false;
        product.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
