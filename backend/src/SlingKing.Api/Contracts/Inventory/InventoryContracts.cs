using System.ComponentModel.DataAnnotations;
using SlingKing.Domain.Common;

namespace SlingKing.Api.Contracts.Inventory;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record MovementRequest(
    [Required] long? VariantId,
    [Required] InventoryTransactionType? Type,
    [Required, Range(1, 1_000_000)] int? Quantity,
    [Range(0, 9_999_999)] decimal? UnitCost,
    [MaxLength(256)] string? Reason);
