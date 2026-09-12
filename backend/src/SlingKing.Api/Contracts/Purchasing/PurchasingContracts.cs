using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Purchasing;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record SupplierRequest(
    [Required, MinLength(1), MaxLength(256)] string Name,
    [MaxLength(32)] string? Phone);

public record PurchaseLineRequest(
    [Required] long? VariantId,
    [Required, Range(1, 1_000_000)] int? Quantity,
    [Range(0, 9_999_999)] decimal? UnitCost);

public record PurchaseRequest(
    [Required] int? SupplierId,
    [Required] DateTimeOffset? PurchaseDate,
    [MaxLength(1024)] string? Notes,
    List<PurchaseLineRequest>? Lines);
