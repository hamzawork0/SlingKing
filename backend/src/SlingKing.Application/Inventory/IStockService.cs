namespace SlingKing.Application.Inventory;

public interface IStockService
{
    /// <summary>
    /// Appends one ledger row, moves the stock level, and lays down or consumes cost — all in
    /// one transaction, under a row lock on the variant's stock level.
    /// </summary>
    /// <exception cref="Common.AppException">
    /// 400 for a quantity below one, a type this endpoint does not accept, a cost on anything
    /// but an inflow, or a missing reason. 404 for an unknown or inactive variant.
    /// </exception>
    Task<MovementView> RecordMovementAsync(
        MovementInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current quantity under the lock, works out the difference, and records it as
    /// an Adjustment. The current quantity is read inside the transaction on purpose: a caller
    /// computing the delta from a number its page loaded a minute ago posts a wrong adjustment
    /// whenever anything happened in between.
    /// </summary>
    /// <exception cref="Common.AppException">
    /// 400 for a negative count. 404 for an unknown or inactive variant.
    /// </exception>
    Task<RecountResult> RecountAsync(
        long variantId, int countedQuantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row per ACTIVE variant. <paramref name="search"/> matches product name, product
    /// code and SKU — the same fields the products list searches, so the two screens behave
    /// the same way.
    /// </summary>
    /// <param name="lowOnly">
    /// Physical quantity at or under the threshold. Variants with a threshold of zero are
    /// excluded: they would otherwise flag forever and make the filter useless.
    /// </param>
    Task<IReadOnlyList<StockRow>> ListAsync(
        string? search, bool lowOnly, CancellationToken cancellationToken = default);

    /// <summary>Null when the variant does not exist or is retired.</summary>
    Task<StockItemView?> GetAsync(long variantId, CancellationToken cancellationToken = default);
}
