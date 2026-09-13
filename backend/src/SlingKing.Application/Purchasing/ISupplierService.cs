using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierOption>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="AppException"/> with 409 if the name is already taken.</summary>
    Task<SupplierOption> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects a supplier's name or phone. Throws 404 if no supplier has that id, and 409 if
    /// ANOTHER supplier already holds the name — the row being edited is excluded from that
    /// check, so fixing capitalisation does not collide with itself.
    /// </summary>
    Task<SupplierOption> UpdateAsync(
        int id, string name, string? phone, CancellationToken cancellationToken = default);
}
