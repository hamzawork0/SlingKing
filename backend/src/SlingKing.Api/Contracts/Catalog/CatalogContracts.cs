using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Catalog;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record CreateCategoryRequest(
    [Required, MinLength(1), MaxLength(128)] string Name);
