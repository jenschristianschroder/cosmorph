namespace Cosmorph.Application.Worlds;

/// <summary>Bounded, configurable cadence and batch settings for world advancement.</summary>
public sealed record SimulationOptions
{
    /// <summary>Real time that maps to one logical tick. One real minute is one in-game day by default.</summary>
    public TimeSpan RealTimePerTick { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum number of directly simulated ticks in one job execution.</summary>
    public int MaxDirectTicksPerRun { get; init; } = 60;

    /// <summary>Backlogs larger than this use the coarse, compressed catch-up path.</summary>
    public int CoarseThresholdTicks { get; init; } = 120;

    /// <summary>Maximum number of ticks compressed into a single aggregate transition.</summary>
    public int CompressChunkTicks { get; init; } = 500;

    /// <summary>Maximum number of compressed ticks in one job execution.</summary>
    public int MaxCompressedTicksPerRun { get; init; } = 4_000;

    /// <summary>Maximum Worldmind calls per job execution and world.</summary>
    public int MaxModelCallsPerRun { get; init; } = 1;

    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum worlds one job execution advances.</summary>
    public int MaxWorldsPerRun { get; init; } = 25;

    /// <summary>Maximum minute buckets one job execution drains.</summary>
    public int MaxBucketsPerRun { get; init; } = 30;

    public void Validate()
    {
        if (RealTimePerTick <= TimeSpan.Zero || RealTimePerTick > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("RealTimePerTick must be greater than zero and at most one hour.");
        }

        if (MaxDirectTicksPerRun is < 1 or > 10_000
            || CoarseThresholdTicks < MaxDirectTicksPerRun
            || CompressChunkTicks is < 1 or > 2_000
            || MaxCompressedTicksPerRun < CompressChunkTicks
            || MaxModelCallsPerRun is < 0 or > 10
            || MaxWorldsPerRun is < 1 or > 500
            || MaxBucketsPerRun is < 1 or > 1_440)
        {
            throw new InvalidOperationException("Simulation batch options are out of range.");
        }
    }
}
