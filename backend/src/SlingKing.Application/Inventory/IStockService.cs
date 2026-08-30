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
}
