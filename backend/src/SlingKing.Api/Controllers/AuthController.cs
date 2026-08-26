using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SlingKing.Api.Contracts.Auth;
using SlingKing.Application.Auth;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    /// <summary>Named here so Program.cs and the attributes below cannot drift apart.</summary>
    public const string CredentialsRateLimitPolicy = "credentials";

    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>
    /// One immutable document for every failed authentication, shared by "no such user",
    /// "wrong secret" and "dead refresh token". It carries no trace identifier and no
    /// request path, so two failures serialise to identical bytes — a per-request traceId
    /// would hand an attacker exactly the email oracle a different status code would.
    /// </summary>
    private static readonly ProblemDetails Invalid = new()
    {
        Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
        Title = "Invalid credentials.",
        Detail = "The details supplied are not valid.",
        Status = StatusCodes.Status401Unauthorized
    };

    private static ObjectResult InvalidCredentials() =>
        new(Invalid)
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/problem+json" }
        };

    private static LoginResponse ToResponse(AuthTokens tokens) => new(
        tokens.AccessToken,
        tokens.RefreshToken,
        tokens.ExpiresAtUtc,
        new UserDto(tokens.User.Id, tokens.User.Email, tokens.User.DisplayName));

    [HttpPost("login")]
    [EnableRateLimiting(CredentialsRateLimitPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _auth.LoginAsync(request.Email, request.Password, cancellationToken);

        return tokens is null ? InvalidCredentials() : Ok(ToResponse(tokens));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(CredentialsRateLimitPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _auth.RefreshAsync(request.RefreshToken, cancellationToken);

        return tokens is null ? InvalidCredentials() : Ok(ToResponse(tokens));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(request.RefreshToken, cancellationToken);

        // Always 204: logging out an already-dead token is not an error worth reporting,
        // and reporting it would say whether the token was ever real.
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

        if (!long.TryParse(subject, out var userId))
        {
            return InvalidCredentials();
        }

        var user = await _auth.GetActiveUserAsync(userId, cancellationToken);

        // A token signed for a user who has since been deactivated or deleted is no longer
        // a credential, however valid its signature still is.
        return user is null
            ? InvalidCredentials()
            : Ok(new UserDto(user.Id, user.Email, user.DisplayName));
    }
}
