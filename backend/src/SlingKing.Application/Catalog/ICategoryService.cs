using SlingKing.Application.Common;

namespace SlingKing.Application.Catalog;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryOption>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="AppException"/> with 409 if the name is already taken.</summary>
    Task<CategoryOption> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects a category's name. Throws 404 if no category has that id, and 409 if ANOTHER
    /// category already holds the name — the row being renamed is excluded from that check, so
    /// fixing capitalisation does not collide with itself.
    /// </summary>
    Task<CategoryOption> RenameAsync(
        int id, string name, CancellationToken cancellationToken = default);
}
