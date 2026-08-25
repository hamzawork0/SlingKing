using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    private readonly IAppDbContext _db;

    public HealthController(IAppDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var databaseReachable = await _db.Database.CanConnectAsync(cancellationToken);

        if (!databaseReachable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "degraded", database = "unreachable" });
        }

        return Ok(new { status = "ok", database = "ok" });
    }
}
