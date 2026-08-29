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

        if (direction < 0 && level.PhysicalQty < input.Quantity)
        {
            throw AppException.Conflict(
                $"Only {level.PhysicalQty} in stock. Record a recount first if that is wrong.");
        }

        var movement = await AppendAsync(
            level, input.Type, direction * input.Quantity, input.UnitCost, input.Reason,
            allowShortfall: false, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return movement;
    }

    /// <summary>
    /// One ledger row, one level update, and the matching cost movement. Assumes the caller
    /// holds the lock on <paramref name="level"/> and has an open transaction — it commits
    /// nothing itself, so a recount can compute its delta and append inside the same one.
    /// </summary>
    /// <param name="signedQuantity">Positive for stock coming in, negative for stock going out.</param>
    private async Task<MovementView> AppendAsync(
        StockLevel level,
        InventoryTransactionType type,
        int signedQuantity,
        decimal? unitCost,
        string? reason,
        bool allowShortfall,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var ledgerRow = new InventoryTransaction
        {
            VariantId = level.VariantId,
            LocationId = level.LocationId,
            Type = type,
            PhysicalDelta = signedQuantity,
            ReservedDelta = 0,
            UnitCost = unitCost,
            TotalCost = unitCost * Math.Abs(signedQuantity),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAt = now,
            CreatedAt = now
        };

        _db.InventoryTransactions.Add(ledgerRow);
        level.PhysicalQty += signedQuantity;
        level.UpdatedAt = now;

        // The consumption rows reference this row's id, so it has to exist first. Still one
        // transaction — two saves inside it, not two transactions.
        await _db.SaveChangesAsync(cancellationToken);

        if (signedQuantity > 0)
        {
            _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
                level.VariantId, level.LocationId, SourceOf(type), ledgerRow.Id,
                signedQuantity, unitCost, now));
        }
        else
        {
            await ConsumeAsync(
                level, ledgerRow, -signedQuantity, allowShortfall, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new MovementView(
            ledgerRow.Id, ledgerRow.OccurredAt, ledgerRow.Type,
            ledgerRow.PhysicalDelta, ledgerRow.TotalCost, ledgerRow.Reason);
    }

    private static CostLayerSourceType SourceOf(InventoryTransactionType type) =>
        type == InventoryTransactionType.OpeningStock
            ? CostLayerSourceType.OpeningStock
            : CostLayerSourceType.AdjustmentIn;

    private async Task ConsumeAsync(
        StockLevel level,
        InventoryTransaction ledgerRow,
        int quantity,
        bool allowShortfall,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The order here IS the FIFO order, and ix_layers_fifo is the index that serves it.
        // The engine trusts this order and does not re-sort.
        var openLayers = await _db.InventoryCostLayers
            .Where(l => l.VariantId == level.VariantId
                     && l.LocationId == level.LocationId
                     && l.RemainingQty > 0)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var plan = FifoCostEngine.Consume(openLayers, quantity, allowShortfall);
        var byId = openLayers.ToDictionary(l => l.Id);

        foreach (var draw in plan.Draws)
        {
            byId[draw.LayerId].RemainingQty -= draw.Quantity;

            _db.InventoryCostConsumptions.Add(new InventoryCostConsumption
            {
                CostLayerId = draw.LayerId,
                InventoryTransactionId = ledgerRow.Id,
                Quantity = draw.Quantity,
                ReversedQty = 0,
                UnitCost = draw.UnitCost,
                TotalCost = draw.UnitCost * draw.Quantity,
                ConsumedAt = now
            });
        }

        ledgerRow.TotalCost = plan.TotalCost;

        // A single rate only when every layer drawn shared one. Averaging two rates would
        // print a per-unit figure that no bag was ever bought at.
        ledgerRow.UnitCost = plan.Draws.Select(d => d.UnitCost).Distinct().Count() == 1
            ? plan.Draws[0].UnitCost
            : null;
    }
}
