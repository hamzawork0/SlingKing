using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private const int MinimumSecretBytes = 32;

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        var keyBytes = Encoding.UTF8.GetBytes(_options.Secret ?? "");

        if (keyBytes.Length < MinimumSecretBytes)
        {
            throw new InvalidOperationException(
                $"SLINGKING_JWT_SECRET must be at least {MinimumSecretBytes} bytes. " +
                "Generate one with: openssl rand -base64 48");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public string CreateAccessToken(long userId, string email)
    {
        var issuedAt = _clock.UtcNow.UtcDateTime;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: issuedAt.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var raw = RandomNumberGenerator.GetBytes(48);
        var token = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
