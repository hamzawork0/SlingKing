using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Auth;

// Attributes here bind to the primary-constructor parameters, which is where ASP.NET Core
// insists validation metadata for a record lives: a [property:] target compiles, then throws
// at request time with "validation metadata ... that will be ignored". Do not "fix" it.
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(1)] string Password);

public record RefreshRequest([Required, MinLength(1)] string RefreshToken);

public record UserDto(long Id, string Email, string DisplayName);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    UserDto User);
