using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

/// <summary>
/// The purchase money arithmetic, and nothing else. Static and database-free on purpose: it
/// keeps a millisecond test loop only for as long as it never learns what an IAppDbContext is.
/// </summary>
public static class PurchaseTotals
{
    /// <summary>Null when the cost is unknown — never zero, which would claim the bags were free.</summary>
    public static decimal? LineTotal(int quantity, decimal? unitCost) => unitCost * quantity;

    /// <summary>
    /// Null when ANY line has an unknown cost, and null for an empty purchase. Never a partial
    /// sum: a total that silently omits a line understates what was paid while still reading
    /// as a figure you can trust.
    /// </summary>
    public static decimal? Total(IReadOnlyList<PurchaseLineInput> lines)
    {
        if (lines.Count == 0 || lines.Any(l => l.UnitCost is null))
        {
            return null;
        }

        return lines.Sum(l => l.UnitCost!.Value * l.Quantity);
    }

    /// <summary>
    /// Two lines for the same colour is a typing mistake, not a quantity of two.
    /// <paramref name="labels"/> maps variant id to a human name so the refusal can say which
    /// one; a missing entry falls back to a sentence that still reads.
    /// </summary>
    public static void RejectDuplicateVariants(
        IReadOnlyList<PurchaseLineInput> lines, IReadOnlyDictionary<long, string> labels)
    {
        var seen = new HashSet<long>();

        foreach (var line in lines)
        {
            if (seen.Add(line.VariantId))
            {
                continue;
            }

            var name = labels.TryGetValue(line.VariantId, out var label) ? label : "That item";

            throw AppException.Invalid(
                $"{name} appears twice. Put the whole quantity on one line.");
        }
    }
}
