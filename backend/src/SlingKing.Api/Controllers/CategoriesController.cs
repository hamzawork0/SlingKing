using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Catalog;
using SlingKing.Application.Catalog;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories) => _categories = categories;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _categories.ListAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(CategoryOption), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        // MinLength(1) accepts "   ", which would create a category with a blank name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        var created = await _categories.CreateAsync(request.Name, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CategoryOption), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(
        int id, RenameCategoryRequest request, CancellationToken cancellationToken)
    {
        // As with Create: MinLength(1) accepts "   ", which would blank the name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _categories.RenameAsync(id, request.Name, cancellationToken));
    }
}
