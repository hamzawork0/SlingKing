using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Purchasing;
using SlingKing.Application.Purchasing;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers) => _suppliers = suppliers;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _suppliers.ListAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(SupplierOption), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        SupplierRequest request, CancellationToken cancellationToken)
    {
        // MinLength(1) accepts "   ", which would create a supplier with a blank name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        var created = await _suppliers.CreateAsync(
            request.Name, request.Phone, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(SupplierOption), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int id, SupplierRequest request, CancellationToken cancellationToken)
    {
        // As with Create: MinLength(1) accepts "   ", which would blank the name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _suppliers.UpdateAsync(
            id, request.Name, request.Phone, cancellationToken));
    }
}
