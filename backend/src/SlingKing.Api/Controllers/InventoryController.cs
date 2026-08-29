using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Inventory;
using SlingKing.Application.Inventory;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IStockService _stock;

    public InventoryController(IStockService stock) => _stock = stock;

    [HttpPost("movements")]
    [ProducesResponseType(typeof(MovementView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordMovement(
        MovementRequest request, CancellationToken cancellationToken)
    {
        var movement = await _stock.RecordMovementAsync(
            new MovementInput(
                request.VariantId!.Value, request.Type!.Value, request.Quantity!.Value,
                request.UnitCost, request.Reason),
            cancellationToken);

        // No GET for a single movement — the item page reads the whole history — so this
        // reports the location of what the caller would actually go and look at.
        return Created($"/api/v1/inventory/{request.VariantId}", movement);
    }
}
