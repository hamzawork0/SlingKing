using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Application.Purchasing;

public sealed class SupplierService : ISupplierService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public SupplierService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<IReadOnlyList<SupplierOption>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Suppliers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption(s.Id, s.Name, s.Phone))
            .ToListAsync(cancellationToken);

    public async Task<SupplierOption> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        var taken = await _db.Suppliers
            .AnyAsync(s => s.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw AppException.Conflict($"A supplier named \"{trimmed}\" already exists.");
        }

        var now = _clock.UtcNow;

        var supplier = new Supplier
        {
            Name = trimmed,
            Phone = Normalise(phone),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierOption(supplier.Id, supplier.Name, supplier.Phone);
    }

    public async Task<SupplierOption> UpdateAsync(
        int id, string name, string? phone, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw AppException.NotFound("That supplier no longer exists.");

        var trimmed = name.Trim();

        // s.Id != id is load-bearing. Correcting capitalisation is the likeliest reason to
        // edit a supplier at all, and without excluding the row being edited it collides with
        // its own existing name and is refused.
        var taken = await _db.Suppliers
            .AnyAsync(s => s.Id != id && s.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw AppException.Conflict($"A supplier named \"{trimmed}\" already exists.");
        }

        supplier.Name = trimmed;
        supplier.Phone = Normalise(phone);
        supplier.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierOption(supplier.Id, supplier.Name, supplier.Phone);
    }
}
