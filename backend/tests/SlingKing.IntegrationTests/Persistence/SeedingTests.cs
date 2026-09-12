using Microsoft.EntityFrameworkCore;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Platform;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

/// <summary>
/// The seeder's contract is a statement about a whole database — "exactly one location,
/// exactly three channels, exactly four sequences" — so every test here starts from a
/// database in a known state. The "database" collection shares one migrated schema and one
/// DbContext across every integration test class, and the other classes leave locations,
/// users and number sequences behind them. Without the reset in InitializeAsync these
/// counts would depend on the order xUnit happened to pick, which is exactly the kind of
/// assertion that passes locally and fails in CI. The reset makes the counts exact rather
/// than approximate; it does not soften them.
/// </summary>
[Collection("database")]
public class SeedingTests : IAsyncLifetime
{
    private const string AdminEmail = "owner@slingking.test";
    private const string AdminPassword = "initial-password-01";

    private readonly TestSchemaFixture _fixture;

    public SeedingTests(TestSchemaFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // The collection runs its classes one at a time, but "one at a time" cuts the
        // other way from what it looks like: it guarantees some class runs AFTER this
        // reset and has to live with whatever it leaves behind. number_sequences must be
        // reset alongside every table that is actually numbered from it — products (the
        // "product" key) and purchases (the "purchase" key) — or a later class allocates
        // a code the sequence has already handed out to a still-surviving row and hits
        // ux_products_code / ux_purchases_number. "order" and "return" exist as keys too,
        // but nothing in this codebase allocates from them yet, so there is no table to
        // pair them with.
        //
        // CASCADE reaches product_variants (off products) and purchase_items (off
        // purchases) through their foreign keys, and RESTART IDENTITY resets those
        // cascaded tables' own id sequences along with the named ones. It does not reach
        // stock_levels, inventory_transactions or inventory_cost_layers: those are keyed
        // by variant_id/location_id with no foreign key at all (deliberately, so stock
        // history survives a deleted product), so once product_variants' identity
        // restarts at 1 a freshly created variant can reuse an id a stale row still
        // references — pk_stock_levels collides, or FIFO cost logic reads someone else's
        // leftover layer. They are named explicitly for that reason; CASCADE off
        // inventory_cost_layers still picks up inventory_cost_consumptions for free.
        _fixture.Db.ChangeTracker.Clear();
        await _fixture.Db.Database.ExecuteSqlRawAsync(
            "truncate table users, locations, sales_channels, number_sequences, " +
            "expense_categories, products, purchases, stock_levels, inventory_transactions, " +
            "inventory_cost_layers restart identity cascade");
    }

    /// <summary>Leaves nothing tracked behind for the next class sharing this context.</summary>
    public Task DisposeAsync()
    {
        _fixture.Db.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    private DatabaseSeeder BuildSeeder() => new(
        _fixture.Db,
        new SystemClock(),
        new BcryptPasswordHasher(),
        adminEmail: AdminEmail,
        adminPassword: AdminPassword);

    /// <summary>
    /// Casts each row of the users table to text, so the scan covers every column of every
    /// row rather than only the one the test happens to think about.
    /// </summary>
    private async Task<int> UserRowsContainingAsync(string needle) =>
        // Runs on the context's own connection, so it spends none of the session pooler's
        // fifteen client slots.
        await _fixture.Db.Database
            .SqlQueryRaw<int>(
                "select count(*)::int as \"Value\" from users u " +
                "where u::text like '%' || {0} || '%'",
                needle)
            .SingleAsync();

    /// <summary>
    /// Every seeded row, whole, as text — timestamps and all. Comparing two of these is a
    /// far stronger idempotence claim than comparing counts: it catches an UPDATE to a
    /// single column, a delete-and-reinsert and a changed identity, none of which move a
    /// row count at all.
    /// </summary>
    private async Task<List<string>> SnapshotAsync()
    {
        var rows = await _fixture.Db.Database
            .SqlQueryRaw<string>(
                "select 'location:' || l::text as \"Value\" from locations l " +
                "union all select 'channel:'  || c::text from sales_channels c " +
                "union all select 'sequence:' || s::text from number_sequences s " +
                "union all select 'expense:'  || e::text from expense_categories e " +
                "union all select 'user:'     || u::text from users u")
            .ToListAsync();

        // Ordered here rather than in SQL: EF wraps the raw query in a subquery, where an
        // ORDER BY is not guaranteed to survive the planner.
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    [Fact]
    public async Task Seeding_creates_the_reference_data_the_system_needs()
    {
        await BuildSeeder().SeedAsync();

        (await _fixture.Db.Locations.CountAsync(l => l.IsDefault)).ShouldBe(1);
        (await _fixture.Db.SalesChannels.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
        (await _fixture.Db.NumberSequences.CountAsync()).ShouldBe(4);
        (await _fixture.Db.ExpenseCategories.CountAsync()).ShouldBeGreaterThanOrEqualTo(4);
        (await _fixture.Db.Users.CountAsync(u => u.Email == AdminEmail)).ShouldBe(1);
    }

    [Fact]
    public async Task The_four_sequence_keys_and_three_channel_codes_are_the_documented_ones()
    {
        await BuildSeeder().SeedAsync();

        (await _fixture.Db.NumberSequences.Select(s => s.Key).ToListAsync())
            .OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(new[] { "order", "product", "purchase", "return" });

        (await _fixture.Db.SalesChannels.Select(c => c.Code).ToListAsync())
            .OrderBy(c => c, StringComparer.Ordinal)
            .ShouldBe(new[] { "instagram", "offline", "whatsapp" });
    }

    [Fact]
    public async Task Offline_is_the_only_channel_that_does_not_reserve()
    {
        await BuildSeeder().SeedAsync();

        var offline = await _fixture.Db.SalesChannels.SingleAsync(c => c.Code == "offline");
        var instagram = await _fixture.Db.SalesChannels.SingleAsync(c => c.Code == "instagram");

        offline.UsesReservation.ShouldBeFalse();
        offline.RequiresShipping.ShouldBeFalse();
        instagram.UsesReservation.ShouldBeTrue();
        instagram.RequiresShipping.ShouldBeTrue();
    }

    [Fact]
    public async Task The_seeded_password_is_stored_hashed_and_verifies()
    {
        await BuildSeeder().SeedAsync();

        var user = await _fixture.Db.Users.SingleAsync(u => u.Email == AdminEmail);

        user.PasswordHash.ShouldNotContain(AdminPassword);
        user.PasswordHash.ShouldStartWith("$2");
        new BcryptPasswordHasher().Verify(AdminPassword, user.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task The_plaintext_password_appears_in_no_column_of_the_users_table()
    {
        await BuildSeeder().SeedAsync();

        // A scan that finds nothing proves nothing until it is shown capable of finding
        // something. Plant the plaintext in a column of a real row and watch the same
        // query pick it up.
        var control = new User
        {
            Email = "control@slingking.test",
            PasswordHash = "not-a-hash",
            DisplayName = AdminPassword,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _fixture.Db.Users.Add(control);
        await _fixture.Db.SaveChangesAsync();

        (await UserRowsContainingAsync(AdminPassword))
            .ShouldBe(1, "the scan is blind — it cannot see plaintext even when it is there");

        _fixture.Db.Users.Remove(control);
        await _fixture.Db.SaveChangesAsync();

        // With the control gone, the seeded row is all that is left — and it is clean.
        (await _fixture.Db.Users.CountAsync()).ShouldBe(1);
        (await UserRowsContainingAsync(AdminPassword)).ShouldBe(0);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await BuildSeeder().SeedAsync();
        await BuildSeeder().SeedAsync();
        await BuildSeeder().SeedAsync();

        (await _fixture.Db.Locations.CountAsync()).ShouldBe(1);
        (await _fixture.Db.SalesChannels.CountAsync()).ShouldBe(3);
        (await _fixture.Db.NumberSequences.CountAsync()).ShouldBe(4);
        (await _fixture.Db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Re_seeding_leaves_every_seeded_row_byte_for_byte_identical()
    {
        await BuildSeeder().SeedAsync();

        var afterFirstRun = await SnapshotAsync();
        // 1 location + 3 channels + 4 sequences + 5 expense categories + 1 user.
        afterFirstRun.Count.ShouldBe(14);

        // The second and third runs are the ones production actually performs, against a
        // database that is already seeded. Neither may insert, update or delete anything.
        await BuildSeeder().SeedAsync();
        await BuildSeeder().SeedAsync();

        (await SnapshotAsync()).ShouldBe(afterFirstRun);
    }

    [Fact]
    public async Task Re_seeding_never_resets_an_already_allocated_sequence()
    {
        await BuildSeeder().SeedAsync();

        var sequence = await _fixture.Db.NumberSequences.SingleAsync(s => s.Key == "order");
        sequence.NextValue = 57;
        await _fixture.Db.SaveChangesAsync();

        await BuildSeeder().SeedAsync();

        (await _fixture.Db.NumberSequences.AsNoTracking()
            .SingleAsync(s => s.Key == "order")).NextValue.ShouldBe(57);
    }

    [Fact]
    public async Task A_failure_partway_through_seeding_never_carries_the_password()
    {
        var seeder = new DatabaseSeeder(
            _fixture.Db,
            new SystemClock(),
            new ThrowingPasswordHasher(),
            adminEmail: AdminEmail,
            adminPassword: AdminPassword);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => seeder.SeedAsync());

        // The seeder neither wraps the failure with the credential it was handling nor
        // records it anywhere else on the way out. It takes no logger at all, so there is
        // no other path by which the plaintext could reach an operator's screen.
        error.ToString().ShouldNotContain(AdminPassword);

        // It throws before SaveChangesAsync, so no half-seeded database is committed.
        (await _fixture.Db.Users.CountAsync()).ShouldBe(0);
        (await _fixture.Db.SalesChannels.CountAsync()).ShouldBe(0);
    }

    private sealed class ThrowingPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) =>
            throw new InvalidOperationException("the password hasher is unavailable");

        public bool Verify(string password, string hash) => false;
    }
}
