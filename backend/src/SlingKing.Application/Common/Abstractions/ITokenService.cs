namespace SlingKing.Application.Common.Abstractions;

public interface ITokenService
{
    string CreateAccessToken(long userId, string email);

    /// <summary>
    /// The raw token is handed to the client exactly once. Only the hash is ever persisted,
    /// so a leaked database does not hand an attacker usable sessions.
    /// </summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}
