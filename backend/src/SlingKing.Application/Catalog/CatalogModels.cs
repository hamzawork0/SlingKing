namespace SlingKing.Application.Catalog;

/// <summary>
/// A failure the caller caused and is allowed to read about. Every message on this type is
/// written by us and carries nothing from an inner exception, a SQL fragment or a type name,
/// which is what makes it safe for <c>ProblemDetailsExceptionHandler</c> to return verbatim.
/// Anything else thrown still becomes a generic 500.
/// </summary>
public sealed class CatalogException : Exception
{
    public int StatusCode { get; }

    private CatalogException(int statusCode, string message, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    public static CatalogException NotFound(string message) => new(404, message);

    /// <summary>
    /// <paramref name="inner"/> is for a rejection we inferred from a database failure rather
    /// than checked ourselves. It never reaches the client — the handler logs it so an
    /// unexpected failure is not silently dressed up as an ordinary conflict.
    /// </summary>
    public static CatalogException Conflict(string message, Exception? inner = null) =>
        new(409, message, inner);

    public static CatalogException Invalid(string message) => new(400, message);
}

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
