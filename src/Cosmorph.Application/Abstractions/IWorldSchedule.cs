using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Abstractions;

/// <summary>
/// Minute-bucket scheduling. A persisted watermark lets an outage be caught up without scanning
/// every world in the account.
/// </summary>
public interface IWorldSchedule
{
    /// <summary>Marks a world as due in the bucket containing <paramref name="instant"/>. Idempotent.</summary>
    Task MarkDueAsync(WorldId worldId, DateTimeOffset instant, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorldId>> ReadBucketAsync(DateTimeOffset bucket, int limit, CancellationToken cancellationToken);

    Task<DateTimeOffset?> TryGetWatermarkAsync(CancellationToken cancellationToken);

    Task SetWatermarkAsync(DateTimeOffset watermark, CancellationToken cancellationToken);
}
