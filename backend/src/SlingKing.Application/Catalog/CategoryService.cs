using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common;
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
            throw AppException.Conflict($"A category named \"{trimmed}\" already exists.");
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

    public async Task<CategoryOption> RenameAsync(
        int id, string name, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw AppException.NotFound("That category does not exist.");

        var trimmed = name.Trim();

        // c.Id != id is load-bearing. Correcting capitalisation is the likeliest reason to
        // rename a category at all, and without excluding the row being renamed it collides
        // with its own existing name and is refused.
        var taken = await _db.Categories
            .AnyAsync(c => c.Id != id && c.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw AppException.Conflict($"A category named \"{trimmed}\" already exists.");
        }

        category.Name = trimmed;
        await _db.SaveChangesAsync(cancellationToken);

        return new CategoryOption(category.Id, category.Name);
    }
}
