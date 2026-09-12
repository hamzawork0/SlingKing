using SlingKing.Domain.Common;

namespace SlingKing.Application.Purchasing;

/// <summary>
/// One line as the client submits it. <c>UnitCost</c> null means the cost is genuinely
/// unknown — a mixed lot bought for one price is real, and inventing a number is the one
/// thing this design forbids everywhere else.
/// </summary>
public record PurchaseLineInput(long VariantId, int Quantity, decimal? UnitCost);

public record SupplierOption(int Id, string Name, string? Phone);

public record PurchaseInput(
    int SupplierId,
    DateTimeOffset PurchaseDate,
    string? Notes,
    IReadOnlyList<PurchaseLineInput> Lines);

public record PurchaseLineView(
    long Id,
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int Quantity,
    decimal? UnitCost,
    decimal? LineTotal);

/// <summary>
/// <c>TotalAmount</c> is null when ANY line has an unknown cost — never a partial sum.
/// </summary>
public record PurchaseView(
    long Id,
    string Number,
    int SupplierId,
    string SupplierName,
    DateTimeOffset PurchaseDate,
    PurchaseStatus Status,
    decimal? TotalAmount,
    string? Notes,
    IReadOnlyList<PurchaseLineView> Lines);
