namespace SlingKing.Application.Catalog;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryOption>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="CatalogException"/> with 409 if the name is already taken.</summary>
    Task<CategoryOption> CreateAsync(string name, CancellationToken cancellationToken = default);
}
