namespace SlingKing.Application.Common.Abstractions;

/// <summary>
/// Allocates the next document number for a sequence key ("order", "purchase",
/// "return", "product"). Gap-free and safe under concurrency.
/// </summary>
public interface INumberSequenceAllocator
{
    Task<string> NextAsync(string key, CancellationToken cancellationToken = default);
}
