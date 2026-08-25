namespace SlingKing.Application.Common.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Returns false — never throws — for a malformed or unrecognised hash.</summary>
    bool Verify(string password, string hash);
}
