namespace Cosmorph.Application.Abstractions;

/// <summary>Time source. Domain and application code never read the wall clock directly.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>System clock used at runtime.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
