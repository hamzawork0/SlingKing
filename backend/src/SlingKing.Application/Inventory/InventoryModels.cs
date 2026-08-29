using SlingKing.Domain.Common;

namespace SlingKing.Application.Inventory;

/// <summary>How much was taken from one layer, and at what rate. UnitCost null means the
/// layer's cost was never known — see <see cref="ConsumptionPlan.TotalCost"/>.</summary>
public readonly record struct LayerDraw(long LayerId, int Quantity, decimal? UnitCost);

/// <summary>
/// What an outflow would take, before anything is written. <c>Drawn</c> is less than the
/// requested quantity only when the caller allowed a shortfall — see
/// <c>FifoCostEngine.Consume</c>.
/// </summary>
/// <param name="TotalCost">
/// Null when ANY drawn layer has an unknown unit cost. Never a partial sum: a partial total
/// understates the real cost and reads on screen as a fact.
/// </param>
public sealed record ConsumptionPlan(
    IReadOnlyList<LayerDraw> Draws, int Drawn, decimal? TotalCost);

/// <summary>
/// <c>Quantity</c> is ALWAYS positive; direction comes from <c>Type</c>. A signed quantity
/// lets -5 on a Damage mean "five damaged" to a person and "five arriving" to the server,
/// and after the fact the two are indistinguishable.
/// </summary>
public record MovementInput(
    long VariantId,
    InventoryTransactionType Type,
    int Quantity,
    decimal? UnitCost,
    string? Reason);

public record MovementView(
    long Id,
    DateTimeOffset OccurredAt,
    InventoryTransactionType Type,
    int PhysicalDelta,
    decimal? TotalCost,
    string? Reason);
