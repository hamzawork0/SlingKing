using SlingKing.Application.Common;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;

namespace SlingKing.Application.Inventory;

/// <summary>
/// The FIFO cost arithmetic, and nothing else. Static and database-free on purpose: it is
/// the riskiest code in Release 1, and it only keeps a millisecond test loop for as long as
/// it never learns what an IAppDbContext is. It reads no stock levels and appends no ledger
/// rows — StockService applies what this returns.
/// </summary>
public static class FifoCostEngine
{
    /// <summary>
    /// Walks <paramref name="openLayers"/> in the order given — which the caller must have
    /// taken from ix_layers_fifo, oldest first — and reports what an outflow of
    /// <paramref name="quantity"/> would take from each.
    /// </summary>
    /// <param name="allowShortfall">
    /// False for every ordinary outflow: running out of layers means the cost records have
    /// diverged from the stock records, and the honest response is to refuse. True only for a
    /// recount, which is the one operation authorised to bring the two back into line — the
    /// counted number is authoritative by definition. Without that exemption the refusal
    /// message names a fix that hits this same wall and throws this same message.
    /// </param>
    public static ConsumptionPlan Consume(
        IReadOnlyList<InventoryCostLayer> openLayers, int quantity, bool allowShortfall)
    {
        if (quantity <= 0)
        {
            // Not an AppException: a caller reaching here with zero has a bug, and dressing
            // that up as a 400 would hide it behind a message the owner cannot act on.
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A consumption must take at least one unit.");
        }

        var draws = new List<LayerDraw>();
        var outstanding = quantity;

        foreach (var layer in openLayers)
        {
            if (outstanding == 0)
            {
                break;
            }

            var take = Math.Min(outstanding, layer.RemainingQty);

            if (take <= 0)
            {
                // A layer with nothing left should not have been passed in, but skipping it
                // is cheaper than a zero-quantity draw that ck_consumption_quantity_positive
                // would reject at the far end of the transaction.
                continue;
            }

            draws.Add(new LayerDraw(layer.Id, take, layer.UnitCost));
            outstanding -= take;
        }

        if (outstanding > 0 && !allowShortfall)
        {
            throw AppException.Conflict(
                "Stock and cost records disagree for this item — run a recount to correct it.");
        }

        return new ConsumptionPlan(draws, quantity - outstanding, TotalOf(draws));
    }

    /// <summary>
    /// Null when any draw has an unknown rate, and null when nothing was drawn. A partial sum
    /// understates the real cost and reads on screen as a fact rather than as a gap.
    /// </summary>
    private static decimal? TotalOf(IReadOnlyList<LayerDraw> draws)
    {
        if (draws.Count == 0 || draws.Any(d => d.UnitCost is null))
        {
            return null;
        }

        return draws.Sum(d => d.UnitCost!.Value * d.Quantity);
    }

    /// <summary>
    /// A new queue entry, full and unconsumed. <paramref name="unitCost"/> null means the cost
    /// is genuinely unknown — a recount, for instance, says how many were counted and nothing
    /// about what they cost.
    /// </summary>
    public static InventoryCostLayer AddLayer(
        long variantId,
        int locationId,
        CostLayerSourceType sourceType,
        long sourceId,
        int quantity,
        decimal? unitCost,
        DateTimeOffset now) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        SourceType = sourceType,
        SourceId = sourceId,
        ReceivedAt = now,
        OriginalQty = quantity,
        RemainingQty = quantity,
        UnitCost = unitCost,
        CreatedAt = now
    };
}
