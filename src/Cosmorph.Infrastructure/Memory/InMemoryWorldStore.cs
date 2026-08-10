using System.Collections.Concurrent;
using System.Globalization;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Serialization;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Memory;

/// <summary>
/// In-memory world store used for local development and tests. It reproduces the durability
/// behaviour of the Blob store: immutable snapshots and events, token-guarded manifests and leases.
/// </summary>
public sealed class InMemoryWorldStore : IWorldStore
{
    private readonly ConcurrentDictionary<string, WorldRecord> _worlds = new(StringComparer.Ordinal);

    public Task<ManifestWithToken?> TryGetManifestAsync(WorldId worldId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_worlds.TryGetValue(worldId.Value, out var record))
        {
            return Task.FromResult<ManifestWithToken?>(null);
        }

        lock (record.Gate)
        {
            return Task.FromResult<ManifestWithToken?>(new ManifestWithToken(record.Manifest, record.ConcurrencyToken));
        }
    }

    public Task<IReadOnlyList<WorldManifest>> ListPublicWorldsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldManifest> manifests =
        [
            .. _worlds.Values
                .Select(r =>
                {
                    lock (r.Gate)
                    {
                        return r.Manifest;
                    }
                })
                .Where(m => m.IsPublic)
                .OrderBy(m => m.Id.Value, StringComparer.Ordinal)
        ];

        return Task.FromResult(manifests);
    }

    public Task<WorldState?> TryLoadSnapshotAsync(WorldId worldId, long tick, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_worlds.TryGetValue(worldId.Value, out var record) || !record.Snapshots.TryGetValue(tick, out var json))
        {
            return Task.FromResult<WorldState?>(null);
        }

        var state = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(json));
        return Task.FromResult<WorldState?>(state);
    }

    public Task WriteSnapshotAsync(WorldState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(state.Id);
        record.Snapshots[state.Tick.Value] = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));
        return Task.CompletedTask;
    }

    public Task AppendEventsAsync(WorldId worldId, IReadOnlyList<WorldEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(worldId);
        lock (record.Gate)
        {
            foreach (var worldEvent in events.OrderBy(e => e.Sequence))
            {
                // Retrying an uncertain write must not duplicate history.
                if (worldEvent.Sequence <= record.HighestSequence)
                {
                    continue;
                }

                record.Events.Add(CanonicalJson.Serialize(EventCodec.ToDocument(worldEvent)));
                record.HighestSequence = worldEvent.Sequence;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorldEvent>> ReadEventsAsync(WorldId worldId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (!_worlds.TryGetValue(worldId.Value, out var record))
        {
            return Task.FromResult<IReadOnlyList<WorldEvent>>([]);
        }

        lock (record.Gate)
        {
            IReadOnlyList<WorldEvent> events =
            [
                .. record.Events
                    .Select(json => EventCodec.ToEvent(CanonicalJson.Deserialize<EventDocument>(json)))
                    .Where(e => e.Sequence > afterSequence)
                    .OrderBy(e => e.Sequence)
                    .Take(limit)
            ];

            return Task.FromResult(events);
        }
    }

    public Task<bool> TryCreateAsync(WorldState state, WorldManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var record = new WorldRecord(manifest);
        if (!_worlds.TryAdd(state.Id.Value, record))
        {
            return Task.FromResult(false);
        }

        record.Snapshots[state.Tick.Value] = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));
        return Task.FromResult(true);
    }

    public Task<bool> TryReplaceManifestAsync(WorldManifest manifest, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(manifest.Id);
        lock (record.Gate)
        {
            if (!string.Equals(record.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            record.Manifest = manifest;
            record.ConcurrencyToken = Guid.NewGuid().ToString("N");
            return Task.FromResult(true);
        }
    }

    public Task<IWorldLease?> TryAcquireLeaseAsync(WorldId worldId, TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(worldId);
        return Task.FromResult(record.Lease.Wait(0, CancellationToken.None) ? new MemoryLease(worldId, record.Lease) : null as IWorldLease);
    }

    public Task WriteDecisionAuditAsync(DecisionAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        var world = Require(record.WorldId);
        world.Decisions[record.Fingerprint] = record;
        return Task.CompletedTask;
    }

    public Task<DecisionAuditRecord?> TryGetDecisionAsync(WorldId worldId, string fingerprint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_worlds.TryGetValue(worldId.Value, out var record))
        {
            return Task.FromResult<DecisionAuditRecord?>(null);
        }

        record.Decisions.TryGetValue(fingerprint, out var decision);
        return Task.FromResult(decision?.Result == DecisionValidationResult.Accepted ? decision : null);
    }

    public Task<bool> TryWriteCommandAsync(WorldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(command.WorldId);
        return Task.FromResult(record.Commands.TryAdd(command.CommandId, command));
    }

    public Task<IReadOnlyList<WorldCommand>> ReadPendingCommandsAsync(WorldId worldId, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (!_worlds.TryGetValue(worldId.Value, out var record))
        {
            return Task.FromResult<IReadOnlyList<WorldCommand>>([]);
        }

        IReadOnlyList<WorldCommand> pending =
        [
            .. record.Commands.Values
                .Where(c => !record.AppliedCommands.ContainsKey(c.CommandId))
                .OrderBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.CommandId, StringComparer.Ordinal)
                .Take(limit)
        ];

        return Task.FromResult(pending);
    }

    public Task MarkCommandsAppliedAsync(WorldId worldId, IReadOnlyList<string> commandIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(worldId);
        foreach (var id in commandIds)
        {
            record.AppliedCommands[id] = true;
        }

        return Task.CompletedTask;
    }

    private WorldRecord Require(WorldId worldId) =>
        _worlds.TryGetValue(worldId.Value, out var record)
            ? record
            : throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"World '{worldId.Value}' does not exist."));

    private sealed class WorldRecord(WorldManifest manifest)
    {
        public object Gate { get; } = new();

        public WorldManifest Manifest { get; set; } = manifest;

        public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

        public ConcurrentDictionary<long, string> Snapshots { get; } = new();

        public List<string> Events { get; } = [];

        public long HighestSequence { get; set; }

        public ConcurrentDictionary<string, DecisionAuditRecord> Decisions { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, WorldCommand> Commands { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, bool> AppliedCommands { get; } = new(StringComparer.Ordinal);

        public SemaphoreSlim Lease { get; } = new(1, 1);
    }

    private sealed class MemoryLease(WorldId worldId, SemaphoreSlim gate) : IWorldLease
    {
        public WorldId WorldId { get; } = worldId;

        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
