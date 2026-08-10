using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Scheduling;

/// <summary>Summary of one bounded job execution.</summary>
public sealed record TickRunSummary
{
    public required int BucketsProcessed { get; init; }

    public required int WorldsAdvanced { get; init; }

    public required int WorldsSkipped { get; init; }

    public required long DirectTicks { get; init; }

    public required long CompressedTicks { get; init; }

    public required int ModelCalls { get; init; }

    public required DateTimeOffset Watermark { get; init; }
}

/// <summary>
/// Drains a bounded number of minute buckets and worlds, then exits. Durable work left behind is
/// picked up by the next scheduled execution.
/// </summary>
public sealed class TickRunner(
    IWorldSchedule schedule,
    WorldAdvancer advancer,
    IClock clock,
    SimulationOptions options)
{
    private readonly IWorldSchedule _schedule = schedule;
    private readonly WorldAdvancer _advancer = advancer;
    private readonly IClock _clock = clock;
    private readonly SimulationOptions _options = options;

    public static DateTimeOffset Bucket(DateTimeOffset instant) =>
        new(instant.UtcDateTime.Date.AddHours(instant.UtcDateTime.Hour).AddMinutes(instant.UtcDateTime.Minute), TimeSpan.Zero);

    public async Task<TickRunSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        _options.Validate();

        var now = Bucket(_clock.UtcNow);
        var watermark = await _schedule.TryGetWatermarkAsync(cancellationToken).ConfigureAwait(false) ?? now.AddMinutes(-1);
        var cursor = Bucket(watermark);

        var buckets = 0;
        var advanced = 0;
        var skipped = 0;
        long direct = 0;
        long compressed = 0;
        var modelCalls = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var processedTo = cursor;

        while (cursor <= now && buckets < _options.MaxBucketsPerRun && seen.Count < _options.MaxWorldsPerRun)
        {
            var worlds = await _schedule.ReadBucketAsync(cursor, _options.MaxWorldsPerRun, cancellationToken).ConfigureAwait(false);
            foreach (var worldId in worlds)
            {
                if (seen.Count >= _options.MaxWorldsPerRun)
                {
                    break;
                }

                if (!seen.Add(worldId.Value))
                {
                    continue;
                }

                var result = await _advancer.AdvanceAsync(worldId, cancellationToken).ConfigureAwait(false);
                direct += result.DirectTicks;
                compressed += result.CompressedTicks;
                modelCalls += result.ModelCalls;

                if (result.Status is AdvanceStatus.Advanced or AdvanceStatus.NotDue)
                {
                    advanced++;
                }
                else
                {
                    skipped++;
                }

                if (result.Status is not (AdvanceStatus.NotFound or AdvanceStatus.Paused))
                {
                    // Keep the world scheduled so progression continues without an always-on worker.
                    await _schedule.MarkDueAsync(worldId, _clock.UtcNow.Add(_options.RealTimePerTick), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            processedTo = cursor;
            buckets++;
            cursor = cursor.AddMinutes(1);
        }

        await _schedule.SetWatermarkAsync(processedTo, cancellationToken).ConfigureAwait(false);

        return new TickRunSummary
        {
            BucketsProcessed = buckets,
            WorldsAdvanced = advanced,
            WorldsSkipped = skipped,
            DirectTicks = direct,
            CompressedTicks = compressed,
            ModelCalls = modelCalls,
            Watermark = processedTo,
        };
    }
}
