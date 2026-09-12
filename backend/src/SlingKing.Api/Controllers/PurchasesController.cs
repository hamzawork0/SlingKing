using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Purchasing;
using SlingKing.Application.Purchasing;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/purchases")]
[Authorize]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchases;

    public PurchasesController(IPurchaseService purchases) => _purchases = purchases;

    private static PurchaseInput ToInput(PurchaseRequest request) => new(
        request.SupplierId!.Value,
        request.PurchaseDate!.Value,
        request.Notes,
        (request.Lines ?? [])
            .Select(l => new PurchaseLineInput(
                l.VariantId!.Value, l.Quantity!.Value, l.UnitCost))
            .ToList());

    [HttpPost]
    [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Record(
        PurchaseRequest request, CancellationToken cancellationToken)
    {
        var created = await _purchases.RecordAsync(ToInput(request), cancellationToken);

        // No GET for a single purchase — the list carries the lines — so this points at the
        // list the caller will actually go and look at.
        return Created("/api/v1/purchases", created);
    }
}
