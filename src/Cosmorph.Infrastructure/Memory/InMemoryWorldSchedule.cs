using System.Collections.Concurrent;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Scheduling;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Memory;

/// <summary>In-memory minute-bucket schedule used for local development and tests.</summary>
public sealed class InMemoryWorldSchedule : IWorldSchedule
{
    private readonly ConcurrentDictionary<DateTimeOffset, ConcurrentDictionary<string, byte>> _buckets = new();
    private DateTimeOffset? _watermark;

    public Task MarkDueAsync(WorldId worldId, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = _buckets.GetOrAdd(TickRunner.Bucket(instant), _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        bucket[worldId.Value] = 1;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorldId>> ReadBucketAsync(DateTimeOffset bucket, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (!_buckets.TryGetValue(TickRunner.Bucket(bucket), out var worlds))
        {
            return Task.FromResult<IReadOnlyList<WorldId>>([]);
        }

        IReadOnlyList<WorldId> result =
        [
            .. worlds.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(limit).Select(WorldId.Parse)
        ];

        return Task.FromResult(result);
    }

    public Task<DateTimeOffset?> TryGetWatermarkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_watermark);
    }

    public Task SetWatermarkAsync(DateTimeOffset watermark, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _watermark = watermark;
        return Task.CompletedTask;
    }
}
