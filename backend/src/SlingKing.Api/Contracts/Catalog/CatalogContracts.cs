using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Catalog;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record CreateCategoryRequest(
    [Required, MinLength(1), MaxLength(128)] string Name);

public record VariantRequest(
    long? Id,
    [MaxLength(64)] string? Colour,
    [MaxLength(64)] string? Size,
    [Required, Range(0, 9_999_999)] decimal? SellingPrice,
    [Required, Range(0, int.MaxValue)] int? LowStockThreshold,
    [Required] bool? IsActive);

public record ProductRequest(
    [Required, MinLength(1), MaxLength(256)] string Name,
    int? CategoryId,
    string? Description,
    List<VariantRequest>? Variants);
