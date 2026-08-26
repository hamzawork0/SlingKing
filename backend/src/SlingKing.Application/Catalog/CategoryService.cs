using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Catalog;

namespace SlingKing.Application.Catalog;

public sealed class CategoryService : ICategoryService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CategoryService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CategoryOption>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOption(c.Id, c.Name))
            .ToListAsync(cancellationToken);

    public async Task<CategoryOption> CreateAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        // ux_categories_name is the real guarantee; this check exists only to turn the
        // race's loser into a readable 409 instead of a 500 from a constraint violation.
        var taken = await _db.Categories
            .AnyAsync(c => c.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw CatalogException.Conflict($"A category named \"{trimmed}\" already exists.");
        }

        var category = new Category
        {
            Name = trimmed,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return new CategoryOption(category.Id, category.Name);
    }
}
