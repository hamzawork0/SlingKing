using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Application.Catalog;

namespace SlingKing.Api.Middleware;

/// <summary>
/// The last line of defence: whatever went wrong inside, the client sees RFC 7807 and
/// never an exception type, a SQL fragment or a stack trace. Detail goes to the log.
/// The response carries the trace identifier and nothing else that came from the failure,
/// so an operator can find the logged exception without the caller ever seeing it.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) =>
        _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // A rejected request is not a failure of ours. It is logged at information level and
        // its message — authored in CatalogException, never derived from an inner exception —
        // is returned verbatim. Everything below this block still becomes a generic 500.
        if (exception is CatalogException rejected)
        {
            _logger.LogInformation(
                "Rejected {Method} {Path}: {Reason}",
                context.Request.Method, context.Request.Path, rejected.Message);

            var rejectionProblem = new ProblemDetails
            {
                Status = rejected.StatusCode,
                Title = rejected.Message,
                Instance = context.Request.Path
            };

            context.Response.StatusCode = rejected.StatusCode;

            await context.Response.WriteAsJsonAsync(
                rejectionProblem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        _logger.LogError(exception,
            "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed. The failure has been logged.",
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = problem.Status.Value;

        // WriteAsJsonAsync would otherwise label this application/json. RFC 7807 says
        // application/problem+json, and the clients branch on it.
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
