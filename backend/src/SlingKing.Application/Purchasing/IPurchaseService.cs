using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

public interface IPurchaseService
{
    /// <summary>
    /// Records a purchase, already complete, and raises the stock it brought in — all in one
    /// transaction. There is no draft: the owner buys at the market and carries the bags home,
    /// so the purchase and the arrival are one event.
    /// </summary>
    /// <exception cref="AppException">
    /// 400 for no lines, a quantity below one, a negative cost, or the same variant twice.
    /// 404 for an unknown supplier or a retired variant.
    /// </exception>
    Task<PurchaseView> RecordAsync(
        PurchaseInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses a purchase: its stock goes back out and its cost layers close. The purchase
    /// stays visible with its reversal in the ledger, because a history that shows bags
    /// appearing and vanishing for no stated reason is worse than one that shows the mistake.
    /// </summary>
    /// <exception cref="AppException">
    /// 404 if no purchase has that id. 409 if it is already cancelled, or if any of its stock
    /// has already been sold, damaged or lost.
    /// </exception>
    Task<PurchaseView> CancelAsync(long id, CancellationToken cancellationToken = default);
}
