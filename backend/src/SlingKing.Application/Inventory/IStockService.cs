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
}
