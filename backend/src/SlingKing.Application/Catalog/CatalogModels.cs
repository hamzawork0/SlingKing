namespace SlingKing.Application.Catalog;

public record CategoryOption(int Id, string Name);

/// <summary>
/// One row of the variant table as the client submits it. <c>Id</c> null means "create this
/// one"; an id means "update the one I already have". <c>IsActive</c> is explicit and is
/// never inferred from a row's absence — a variant missing from the list is left untouched,
/// because "missing means delete" is how this pattern silently destroys data.
/// </summary>
public record VariantInput(
    long? Id,
    string? Colour,
    string? Size,
    decimal SellingPrice,
    int LowStockThreshold,
    bool IsActive);

public record ProductInput(
    string Name,
    int? CategoryId,
    string? Description,
    IReadOnlyList<VariantInput> Variants);

public record VariantView(
    long Id,
    string Sku,
    string? Colour,
    string? Size,
    decimal SellingPrice,
    int LowStockThreshold,
    bool IsActive,
    int PhysicalQty);

/// <summary>
/// <c>TotalStock</c> is always 0 until M2b, because nothing can move stock yet. It is read
/// from real <c>stock_levels</c> rows rather than synthesised, which is what the eager
/// zero-quantity rows written at creation buy.
/// </summary>
public record ProductView(
    long Id,
    string Code,
    string Name,
    int? CategoryId,
    string? CategoryName,
    string? Description,
    bool IsActive,
    int TotalStock,
    IReadOnlyList<VariantView> Variants);
