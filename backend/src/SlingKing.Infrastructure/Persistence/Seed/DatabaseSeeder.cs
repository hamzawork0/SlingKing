using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Inventory;
using SlingKing.Domain.Operations;
using SlingKing.Domain.Platform;
using SlingKing.Domain.Sales;

namespace SlingKing.Infrastructure.Persistence.Seed;

/// <summary>
/// Runs on every startup. Every insert is guarded by an existence check, so this is
/// safe to run against a database that already holds a year of real trading.
/// The plaintext admin password is used for exactly one thing — the argument to
/// <see cref="IPasswordHasher.Hash"/> — and is never assigned to an entity, written to
/// a message, or handed to a logger. The class takes no logger for that reason.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;
    private readonly string _adminEmail;
    private readonly string _adminPassword;

    public DatabaseSeeder(
        IAppDbContext db,
        IClock clock,
        IPasswordHasher hasher,
        string adminEmail,
        string adminPassword)
    {
        _db = db;
        _clock = clock;
        _hasher = hasher;
        _adminEmail = adminEmail;
        _adminPassword = adminPassword;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        if (!await _db.Locations.AnyAsync(cancellationToken))
        {
            _db.Locations.Add(new Location { Name = "Main", IsDefault = true, IsActive = true });
        }

        await SeedChannelsAsync(cancellationToken);
        await SeedSequencesAsync(cancellationToken);
        await SeedExpenseCategoriesAsync(cancellationToken);
        await SeedAdminUserAsync(now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedChannelsAsync(CancellationToken cancellationToken)
    {
        // Channel behaviour is data. Offline hands the bag over immediately, so it
        // never reserves and never ships.
        var wanted = new[]
        {
            new SalesChannel { Code = "instagram", Name = "Instagram", UsesReservation = true,  RequiresShipping = true,  SortOrder = 1 },
            new SalesChannel { Code = "whatsapp",  Name = "WhatsApp",  UsesReservation = true,  RequiresShipping = true,  SortOrder = 2 },
            new SalesChannel { Code = "offline",   Name = "Offline",   UsesReservation = false, RequiresShipping = false, SortOrder = 3 }
        };

        var existing = await _db.SalesChannels
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        foreach (var channel in wanted.Where(c => !existing.Contains(c.Code)))
        {
            _db.SalesChannels.Add(channel);
        }
    }

    private async Task SeedSequencesAsync(CancellationToken cancellationToken)
    {
        var wanted = new[]
        {
            new NumberSequence { Key = "product",  Prefix = "BAG", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "purchase", Prefix = "PUR", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "order",    Prefix = "ORD", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "return",   Prefix = "RET", NextValue = 1, Padding = 6 }
        };

        var existing = await _db.NumberSequences
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        // Never touch NextValue on an existing row — that would re-issue document numbers.
        foreach (var sequence in wanted.Where(s => !existing.Contains(s.Key)))
        {
            _db.NumberSequences.Add(sequence);
        }
    }

    private async Task SeedExpenseCategoriesAsync(CancellationToken cancellationToken)
    {
        var wanted = new[] { "Shipping", "Packaging", "Marketing", "Platform fees", "Other" };

        var existing = await _db.ExpenseCategories
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        foreach (var name in wanted.Where(n => !existing.Contains(n)))
        {
            _db.ExpenseCategories.Add(new ExpenseCategory { Name = name, IsActive = true });
        }
    }

    private async Task SeedAdminUserAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_adminEmail) || string.IsNullOrWhiteSpace(_adminPassword))
        {
            return;
        }

        if (await _db.Users.AnyAsync(u => u.Email == _adminEmail, cancellationToken))
        {
            return;
        }

        _db.Users.Add(new User
        {
            Email = _adminEmail,
            PasswordHash = _hasher.Hash(_adminPassword),
            DisplayName = "Owner",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
