using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
