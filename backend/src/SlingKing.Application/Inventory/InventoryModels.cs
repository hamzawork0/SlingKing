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

/// <summary>
/// What a recount did. <c>Movement</c> is null when the count matched — nothing happened,
/// and the ledger should not carry a row saying so.
/// </summary>
public record RecountResult(int Previous, int Counted, int Difference, MovementView? Movement);

/// <summary>
/// One line of the stock list. Carries no money: a value column would make every list
/// render touch the cost layers for a figure the item page already shows.
/// </summary>
public record StockRow(
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int PhysicalQty,
    int LowStockThreshold);

/// <summary>
/// <c>Movements</c> is newest-first, which is the order the page reads them in. There is no
/// running-total column: the client has the whole list and accumulates it itself, rather than
/// the same arithmetic being done twice in two languages.
/// </summary>
/// <param name="StockValue">
/// Null when any open layer has an unknown cost. A partial total would understate the stock.
/// </param>
public record StockItemView(
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int PhysicalQty,
    int LowStockThreshold,
    decimal? StockValue,
    IReadOnlyList<MovementView> Movements);
