using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure.Persistence;

public sealed class NumberSequenceAllocator : INumberSequenceAllocator
{
    private readonly IAppDbContext _db;

    public NumberSequenceAllocator(IAppDbContext db) => _db = db;

    private sealed record Allocation(string Prefix, long Value, int Padding);

    public async Task<string> NextAsync(string key, CancellationToken cancellationToken = default)
    {
        // A single statement: the read, the increment and the claim are one atomic
        // operation, so two concurrent callers can never observe the same value.
        var rows = await _db.Database
            .SqlQuery<Allocation>($@"
                update number_sequences
                   set next_value = next_value + 1
                 where key = {key}
                returning prefix as ""Prefix"",
                          next_value - 1 as ""Value"",
                          padding as ""Padding""")
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"Number sequence '{key}' does not exist. Seed it before allocating from it.");
        }

        var allocation = rows[0];

        return $"{allocation.Prefix}-{allocation.Value.ToString(new string('0', allocation.Padding))}";
    }
}
