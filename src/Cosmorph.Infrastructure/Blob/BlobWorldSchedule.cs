using System.Globalization;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Scheduling;
using Cosmorph.Application.Serialization;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Blob;

/// <summary>Persisted watermark document for one scheduler shard.</summary>
public sealed record WatermarkDocument
{
    public required string Schema { get; init; }

    public required DateTimeOffset WatermarkUtc { get; init; }
}

/// <summary>Blob-backed minute-bucket schedule with a persisted watermark.</summary>
public sealed class BlobWorldSchedule(BlobContainerClient container, int shard = 0) : IWorldSchedule
{
    public const string WatermarkSchema = "scheduler-watermark/1";

    private readonly BlobContainerClient _container = container;
    private readonly int _shard = shard is >= 0 and < 100
        ? shard
        : throw new ArgumentOutOfRangeException(nameof(shard));

    public async Task MarkDueAsync(WorldId worldId, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        var bucket = TickRunner.Bucket(instant);
        var blob = _container.GetBlobClient(BlobPaths.ScheduleMarker(bucket, _shard, worldId));
        try
        {
            // A duplicate marker is harmless and never creates duplicate work.
            await blob.UploadAsync(
                BinaryData.FromString("{}"),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // Already scheduled.
        }
    }

    public async Task<IReadOnlyList<WorldId>> ReadBucketAsync(DateTimeOffset bucket, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var worlds = new List<WorldId>();
        try
        {
            await foreach (var item in _container
                .GetBlobsAsync(BlobTraits.None, BlobStates.None, BlobPaths.SchedulePrefix(TickRunner.Bucket(bucket), _shard), cancellationToken)
                .ConfigureAwait(false))
            {
                if (worlds.Count >= limit)
                {
                    break;
                }

                if (WorldId.TryParse(BlobPaths.WorldIdFromMarker(item.Name), out var worldId))
                {
                    worlds.Add(worldId);
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The container is created with the first world, so before then every bucket is empty.
            // Failing here would abort each scheduled run on a new environment.
            return [];
        }

        return [.. worlds.OrderBy(w => w.Value, StringComparer.Ordinal)];
    }

    public async Task<DateTimeOffset?> TryGetWatermarkAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.GetBlobClient(BlobPaths.Watermark(_shard))
                .DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return CanonicalJson.Deserialize<WatermarkDocument>(response.Value.Content.ToString()).WatermarkUtc;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SetWatermarkAsync(DateTimeOffset watermark, CancellationToken cancellationToken)
    {
        var document = new WatermarkDocument { Schema = WatermarkSchema, WatermarkUtc = watermark.ToUniversalTime() };
        await _container.GetBlobClient(BlobPaths.Watermark(_shard))
            .UploadAsync(BinaryData.FromString(CanonicalJson.Serialize(document)), overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"BlobWorldSchedule(shard={_shard})");
}
