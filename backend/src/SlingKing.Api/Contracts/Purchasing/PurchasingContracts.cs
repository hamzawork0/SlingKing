using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Purchasing;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record SupplierRequest(
    [Required, MinLength(1), MaxLength(256)] string Name,
    [MaxLength(32)] string? Phone);
