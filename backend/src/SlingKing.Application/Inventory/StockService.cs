using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;

namespace SlingKing.Application.Inventory;

public sealed class StockService : IStockService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public StockService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// The four types this API accepts, each with exactly one direction. Adjustment is
    /// deliberately absent: the recount endpoint is its only producer, because an adjustment
    /// a caller computed itself is computed from a number their screen loaded a minute ago.
    /// </summary>
    private static readonly Dictionary<InventoryTransactionType, int> PostableDirections = new()
    {
        [InventoryTransactionType.OpeningStock] = +1,
        [InventoryTransactionType.Damage] = -1,
        [InventoryTransactionType.Loss] = -1,
        [InventoryTransactionType.PersonalUse] = -1
    };

    private static void RejectBadInput(MovementInput input)
    {
        if (input.Quantity <= 0)
        {
            throw AppException.Invalid("Enter how many, as a number above zero.");
        }

        if (!PostableDirections.TryGetValue(input.Type, out var direction))
        {
            throw AppException.Invalid("That kind of movement cannot be recorded here yet.");
        }

        if (input.UnitCost is not null && direction < 0)
        {
            throw AppException.Invalid("A cost can only be set on stock coming in.");
        }

        if (input.UnitCost < 0)
        {
            throw AppException.Invalid("A cost cannot be negative.");
        }

        if (direction < 0 && string.IsNullOrWhiteSpace(input.Reason))
        {
            throw AppException.Invalid("Say briefly what happened, so this makes sense later.");
        }
    }

    private Task<int> DefaultLocationIdAsync(CancellationToken cancellationToken) =>
        _db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync(cancellationToken);

    /// <summary>
    /// Takes the row lock and returns the tracked level. Two statements rather than a
    /// FromSql that does both: the extra round trip inside an already-open transaction costs
    /// nothing, and a plain LINQ read cannot be broken by how EF materialises the generated
    /// available_qty column.
    /// </summary>
    private async Task<StockLevel> LockLevelAsync(
        long variantId, int locationId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from stock_levels where variant_id = {variantId} and location_id = {locationId} for update",
            cancellationToken);

        return await _db.StockLevels.SingleAsync(
            s => s.VariantId == variantId && s.LocationId == locationId, cancellationToken);
    }

    public async Task<MovementView> RecordMovementAsync(
        MovementInput input, CancellationToken cancellationToken = default)
    {
        RejectBadInput(input);

        var variantExists = await _db.ProductVariants
            .AnyAsync(v => v.Id == input.VariantId && v.IsActive, cancellationToken);

        if (!variantExists)
        {
            throw AppException.NotFound("That item no longer exists.");
        }

        var locationId = await DefaultLocationIdAsync(cancellationToken);
        var direction = PostableDirections[input.Type];
        var now = _clock.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var level = await LockLevelAsync(input.VariantId, locationId, cancellationToken);

        var ledgerRow = new InventoryTransaction
        {
            VariantId = input.VariantId,
            LocationId = locationId,
            Type = input.Type,
            PhysicalDelta = direction * input.Quantity,
            ReservedDelta = 0,
            UnitCost = input.UnitCost,
            TotalCost = input.UnitCost * input.Quantity,
            Reason = input.Reason?.Trim(),
            OccurredAt = now,
            CreatedAt = now
        };

        _db.InventoryTransactions.Add(ledgerRow);
        level.PhysicalQty += ledgerRow.PhysicalDelta;
        level.UpdatedAt = now;

        // The ledger row must exist before anything can reference its id.
        await _db.SaveChangesAsync(cancellationToken);

        _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
            input.VariantId, locationId, CostLayerSourceType.OpeningStock,
            ledgerRow.Id, input.Quantity, input.UnitCost, now));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MovementView(
            ledgerRow.Id, ledgerRow.OccurredAt, ledgerRow.Type,
            ledgerRow.PhysicalDelta, ledgerRow.TotalCost, ledgerRow.Reason);
    }
}
