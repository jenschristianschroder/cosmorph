using System.Globalization;
using System.IO.Compression;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Serialization;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Blob;

/// <summary>
/// Azure Blob implementation of <see cref="IWorldStore"/>. The client is always constructed from the
/// Blob service URI and a token credential; connection strings and account keys are never used.
/// </summary>
public sealed class BlobWorldStore(BlobContainerClient container) : IWorldStore
{
    private readonly BlobContainerClient _container = container;

    public async Task<ManifestWithToken?> TryGetManifestAsync(WorldId worldId, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(BlobPaths.Manifest(worldId));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var manifest = CanonicalJson.Deserialize<WorldManifest>(response.Value.Content.ToString());
            return new ManifestWithToken(manifest, response.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<WorldManifest>> ListPublicWorldsAsync(CancellationToken cancellationToken)
    {
        var manifests = new List<WorldManifest>();
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, "worlds/", cancellationToken).ConfigureAwait(false))
        {
            if (!item.Name.EndsWith("/manifest.json", StringComparison.Ordinal))
            {
                continue;
            }

            var content = await _container.GetBlobClient(item.Name)
                .DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var manifest = CanonicalJson.Deserialize<WorldManifest>(content.Value.Content.ToString());
            if (manifest.IsPublic)
            {
                manifests.Add(manifest);
            }
        }

        return [.. manifests.OrderBy(m => m.Id.Value, StringComparer.Ordinal)];
    }

    public async Task<WorldState?> TryLoadSnapshotAsync(WorldId worldId, long tick, CancellationToken cancellationToken)
    {
        var json = await TryDownloadCompressedAsync(BlobPaths.Snapshot(worldId, tick), cancellationToken).ConfigureAwait(false);
        return json is null ? null : SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(json));
    }

    public async Task WriteSnapshotAsync(WorldState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));

        // Snapshots are addressed by tick, so re-writing after an uncertain failure is safe.
        await UploadCompressedAsync(BlobPaths.Snapshot(state.Id, state.Tick.Value), json, overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AppendEventsAsync(WorldId worldId, IReadOnlyList<WorldEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        foreach (var group in events.OrderBy(e => e.Sequence).GroupBy(e => BlobPaths.SegmentOf(e.Sequence)))
        {
            var path = BlobPaths.EventSegment(worldId, group.Key);
            var existing = await TryDownloadCompressedAsync(path, cancellationToken).ConfigureAwait(false) ?? string.Empty;
            var known = new HashSet<long>();
            foreach (var line in existing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                known.Add(CanonicalJson.Deserialize<EventDocument>(line).Sequence);
            }

            var builder = new StringBuilder(existing);
            var added = false;
            foreach (var worldEvent in group)
            {
                if (!known.Add(worldEvent.Sequence))
                {
                    continue;
                }

                builder.Append(CanonicalJson.Serialize(EventCodec.ToDocument(worldEvent))).Append('\n');
                added = true;
            }

            if (added)
            {
                await UploadCompressedAsync(path, builder.ToString(), overwrite: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<WorldEvent>> ReadEventsAsync(WorldId worldId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var results = new List<WorldEvent>(limit);
        var segment = BlobPaths.SegmentOf(Math.Max(0, afterSequence) + 1);

        while (results.Count < limit)
        {
            var content = await TryDownloadCompressedAsync(BlobPaths.EventSegment(worldId, segment), cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                break;
            }

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var worldEvent = EventCodec.ToEvent(CanonicalJson.Deserialize<EventDocument>(line));
                if (worldEvent.Sequence > afterSequence)
                {
                    results.Add(worldEvent);
                }
            }

            segment++;
        }

        return [.. results.OrderBy(e => e.Sequence).Take(limit)];
    }

    public async Task<bool> TryCreateAsync(WorldState state, WorldManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(manifest);

        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await WriteSnapshotAsync(state, cancellationToken).ConfigureAwait(false);

        var blob = _container.GetBlobClient(BlobPaths.Manifest(manifest.Id));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromString(CanonicalJson.Serialize(manifest)),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<bool> TryReplaceManifestAsync(WorldManifest manifest, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var blob = _container.GetBlobClient(BlobPaths.Manifest(manifest.Id));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromString(CanonicalJson.Serialize(manifest)),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfMatch = new ETag(concurrencyToken) } },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<IWorldLease?> TryAcquireLeaseAsync(WorldId worldId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(BlobPaths.Lease(worldId));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromString("{}"),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            // The lease blob already exists, which is the normal case.
        }

        var leaseClient = blob.GetBlobLeaseClient();
        try
        {
            await leaseClient.AcquireAsync(duration, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new BlobWorldLease(worldId, leaseClient);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return null;
        }
    }

    public async Task WriteDecisionAuditAsync(DecisionAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var blob = _container.GetBlobClient(BlobPaths.DecisionAudit(record.WorldId, record.Fingerprint));
        await blob.UploadAsync(BinaryData.FromString(CanonicalJson.Serialize(record)), overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DecisionAuditRecord?> TryGetDecisionAsync(WorldId worldId, string fingerprint, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(BlobPaths.DecisionAudit(worldId, fingerprint));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var record = CanonicalJson.Deserialize<DecisionAuditRecord>(response.Value.Content.ToString());
            return record.Result == DecisionValidationResult.Accepted && record.WorldId == worldId ? record : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> TryWriteCommandAsync(WorldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var blob = _container.GetBlobClient(BlobPaths.Command(command.WorldId, command.CommandId));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromString(CanonicalJson.Serialize(command)),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<WorldCommand>> ReadPendingCommandsAsync(WorldId worldId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var commands = new List<WorldCommand>();
        await foreach (var item in _container
            .GetBlobsAsync(BlobTraits.None, BlobStates.None, BlobPaths.CommandPrefix(worldId), cancellationToken)
            .ConfigureAwait(false))
        {
            if (commands.Count >= limit)
            {
                break;
            }

            var content = await _container.GetBlobClient(item.Name)
                .DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var command = CanonicalJson.Deserialize<WorldCommand>(content.Value.Content.ToString());
            if (command.WorldId != worldId)
            {
                continue;
            }

            var applied = await _container.GetBlobClient(BlobPaths.AppliedCommand(worldId, command.CommandId))
                .ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!applied.Value)
            {
                commands.Add(command);
            }
        }

        return [.. commands.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.CommandId, StringComparer.Ordinal)];
    }

    public async Task MarkCommandsAppliedAsync(WorldId worldId, IReadOnlyList<string> commandIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        foreach (var commandId in commandIds)
        {
            var blob = _container.GetBlobClient(BlobPaths.AppliedCommand(worldId, commandId));
            await blob.UploadAsync(
                BinaryData.FromString($"{{\"appliedAtUtc\":\"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}\"}}"),
                overwrite: true,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryDownloadCompressedAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.GetBlobClient(path)
                .DownloadContentAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            using var source = response.Value.Content.ToStream();
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var reader = new StreamReader(brotli, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task UploadCompressedAsync(string path, string content, bool overwrite, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using (var brotli = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            await brotli.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        await _container.GetBlobClient(path).UploadAsync(buffer, overwrite, cancellationToken).ConfigureAwait(false);
    }

    private sealed class BlobWorldLease(WorldId worldId, BlobLeaseClient lease) : IWorldLease
    {
        public WorldId WorldId { get; } = worldId;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await lease.ReleaseAsync().ConfigureAwait(false);
            }
            catch (RequestFailedException)
            {
                // The lease expired on its own; the next execution will acquire it again.
            }
        }
    }
}
