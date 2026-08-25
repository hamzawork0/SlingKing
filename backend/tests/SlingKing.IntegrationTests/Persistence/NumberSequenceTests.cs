using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SlingKing.Domain.Platform;
using SlingKing.Infrastructure.Persistence;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

[Collection("database")]
public class NumberSequenceTests
{
    private readonly TestSchemaFixture _fixture;

    public NumberSequenceTests(TestSchemaFixture fixture) => _fixture = fixture;

    private async Task<string> SeedSequenceAsync(string prefix, long start = 1, int padding = 6)
    {
        var key = $"seq-{Guid.NewGuid():N}"[..20];

        _fixture.Db.NumberSequences.Add(new NumberSequence
        {
            Key = key, Prefix = prefix, NextValue = start, Padding = padding
        });
        await _fixture.Db.SaveChangesAsync();

        return key;
    }

    [Fact]
    public async Task Formats_the_number_with_prefix_and_padding()
    {
        var key = await SeedSequenceAsync("ORD");
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("ORD-000001");
        (await allocator.NextAsync(key)).ShouldBe("ORD-000002");
    }

    [Fact]
    public async Task Respects_a_starting_value_and_a_custom_padding()
    {
        var key = await SeedSequenceAsync("PUR", start: 47, padding: 4);
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("PUR-0047");
    }

    [Fact]
    public async Task Numbers_do_not_overflow_the_padding()
    {
        var key = await SeedSequenceAsync("RET", start: 1_000_000);
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("RET-1000000");
    }

    [Fact]
    public async Task An_unknown_key_fails_loudly_rather_than_inventing_a_number()
    {
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        await Should.ThrowAsync<InvalidOperationException>(
            () => allocator.NextAsync("no-such-sequence"));
    }

    [Fact]
    public async Task Twenty_concurrent_callers_receive_twenty_distinct_numbers()
    {
        var key = await SeedSequenceAsync("ORD");

        // The Supabase session pooler caps this project at fifteen clients, and twenty
        // racers each demanding their own physical connection trip that limit before
        // they ever reach the sequence. Bound the pool instead of the concurrency:
        // twenty callers still contend for one row, eight of them inside the database
        // at any instant, which is a race a read-then-write allocator loses.
        var connection = new NpgsqlConnectionStringBuilder(
            _fixture.Db.Database.GetConnectionString()!)
        {
            MaxPoolSize = 8
        }.ToString();

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection, o =>
                    o.MigrationsHistoryTable("__ef_migrations_history", TestSchemaFixture.SchemaName))
                .UseSnakeCaseNamingConvention()
                .Options;

            await using var db = new AppDbContext(options);
            return await new NumberSequenceAllocator(db).NextAsync(key);
        });

        var numbers = await Task.WhenAll(tasks);

        numbers.Distinct().Count().ShouldBe(20, "duplicate document numbers were allocated");

        // Gap-free, not merely distinct: twenty allocations from a sequence starting at
        // one must consume exactly one through twenty, with nothing skipped.
        numbers.OrderBy(n => n).ShouldBe(
            Enumerable.Range(1, 20).Select(i => $"ORD-{i:000000}"),
            "the allocated numbers were distinct but not contiguous");
    }
}
