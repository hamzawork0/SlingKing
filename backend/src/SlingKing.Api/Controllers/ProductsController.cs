using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Catalog;
using SlingKing.Application.Catalog;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductService _products;

    public ProductsController(IProductService products) => _products = products;

    private static ProductInput ToInput(ProductRequest request) => new(
        request.Name,
        request.CategoryId,
        request.Description,
        (request.Variants ?? [])
            .Select(v => new VariantInput(
                v.Id, v.Colour, v.Size, v.SellingPrice!.Value, v.LowStockThreshold!.Value,
                v.IsActive!.Value))
            .ToList());

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Ok(await _products.ListAsync(search, includeInactive, cancellationToken));

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ProductView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var product = await _products.GetAsync(id, cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        ProductRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        var created = await _products.CreateAsync(ToInput(request), cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(ProductView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        long id, ProductRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _products.UpdateAsync(id, ToInput(request), cancellationToken));
    }
}
