namespace SlingKing.Application.Purchasing;

/// <summary>
/// One line as the client submits it. <c>UnitCost</c> null means the cost is genuinely
/// unknown — a mixed lot bought for one price is real, and inventing a number is the one
/// thing this design forbids everywhere else.
/// </summary>
public record PurchaseLineInput(long VariantId, int Quantity, decimal? UnitCost);

public record SupplierOption(int Id, string Name, string? Phone);
