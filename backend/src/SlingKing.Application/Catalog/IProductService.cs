namespace SlingKing.Application.Catalog;

public interface IProductService
{
    /// <summary>Null when no product has that id.</summary>
    Task<ProductView?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates the product code, derives a SKU per variant, and writes a zero-quantity
    /// stock row per variant at the default location — all in one transaction. An empty
    /// variant list yields one variant with no colour and no size.
    /// </summary>
    Task<ProductView> CreateAsync(ProductInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Newest first. <paramref name="search"/> matches product name, product code and
    /// variant SKU, case-insensitively. <paramref name="includeInactive"/> applies to
    /// products only — variants always come back, each carrying its own IsActive.
    /// </summary>
    Task<IReadOnlyList<ProductView>> ListAsync(
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken = default);
}
