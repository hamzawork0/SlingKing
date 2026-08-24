namespace SlingKing.Application.Common.Abstractions;

/// <summary>Injected everywhere instead of DateTimeOffset.UtcNow, so time is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
