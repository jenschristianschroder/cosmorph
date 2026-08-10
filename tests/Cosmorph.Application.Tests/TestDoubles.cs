using System.Collections.Concurrent;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Serialization;
using Cosmorph.Application.Scheduling;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Tests;

/// <summary>Fake clock. Tests advance logical time instead of sleeping.</summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan amount) => UtcNow += amount;
}

/// <summary>
/// Minimal world-scoped store used by application tests. It keeps the layering rule that the
/// application layer never depends on infrastructure, while reproducing the durability behaviour the
/// advancer relies on: immutable snapshots, token-guarded manifests, exclusive leases and
/// idempotent commands.
/// </summary>
public sealed class TestWorldStore : IWorldStore
{
    private sealed class Record
    {
        public required WorldManifest Manifest { get; set; }

        public string ConcurrencyToken { get; set; } = "1";

        public bool Leased { get; set; }

        public Dictionary<long, string> Snapshots { get; } = [];

        public List<WorldEvent> Events { get; } = [];

        public Dictionary<string, WorldCommand> Pending { get; } = new(StringComparer.Ordinal);

        public HashSet<string> SeenCommandIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DecisionAuditRecord> Decisions { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<string, Record> _worlds = new(StringComparer.Ordinal);

    public int SnapshotWrites { get; private set; }

    public int ManifestReplacements { get; private set; }

    public IReadOnlyList<DecisionAuditRecord> Audit(WorldId worldId) =>
        [.. Require(worldId).Decisions.Values.OrderBy(d => d.DecisionId, StringComparer.Ordinal)];

    public Task<ManifestWithToken?> TryGetManifestAsync(WorldId worldId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ManifestWithToken?>(_worlds.TryGetValue(worldId.Value, out var record)
            ? new ManifestWithToken(record.Manifest, record.ConcurrencyToken)
            : null);
    }

    public Task<IReadOnlyList<WorldManifest>> ListPublicWorldsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldManifest> manifests =
        [
            .. _worlds.Values.Select(r => r.Manifest).Where(m => m.IsPublic).OrderBy(m => m.Id.Value, StringComparer.Ordinal)
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

        return Task.FromResult<WorldState?>(SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(json)));
    }

    public Task WriteSnapshotAsync(WorldState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(state.Id);
        record.Snapshots[state.Tick.Value] = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));
        SnapshotWrites++;
        return Task.CompletedTask;
    }

    public Task AppendEventsAsync(WorldId worldId, IReadOnlyList<WorldEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(worldId);
        foreach (var candidate in events.OrderBy(e => e.Sequence))
        {
            if (record.Events.Count == 0 || candidate.Sequence > record.Events[^1].Sequence)
            {
                record.Events.Add(candidate);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorldEvent>> ReadEventsAsync(WorldId worldId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldEvent> page =
        [
            .. Require(worldId).Events.Where(e => e.Sequence > afterSequence).OrderBy(e => e.Sequence).Take(limit)
        ];
        return Task.FromResult(page);
    }

    public Task<bool> TryCreateAsync(WorldState state, WorldManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        var record = new Record { Manifest = manifest };
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
        if (RejectManifestReplacements || !string.Equals(record.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        record.Manifest = manifest;
        record.ConcurrencyToken = Guid.NewGuid().ToString("n");
        ManifestReplacements++;
        return Task.FromResult(true);
    }

    /// <summary>Simulates a competing writer that already replaced the manifest.</summary>
    public bool RejectManifestReplacements { get; set; }

    public Task<IWorldLease?> TryAcquireLeaseAsync(WorldId worldId, TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_worlds.TryGetValue(worldId.Value, out var record))
        {
            return Task.FromResult<IWorldLease?>(new Lease(worldId, null));
        }

        lock (record)
        {
            if (record.Leased)
            {
                return Task.FromResult<IWorldLease?>(null);
            }

            record.Leased = true;
            return Task.FromResult<IWorldLease?>(new Lease(worldId, () => record.Leased = false));
        }
    }

    public Task WriteDecisionAuditAsync(DecisionAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        Require(record.WorldId).Decisions[record.Fingerprint] = record;
        return Task.CompletedTask;
    }

    public Task<DecisionAuditRecord?> TryGetDecisionAsync(WorldId worldId, string fingerprint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Require(worldId).Decisions.GetValueOrDefault(fingerprint));
    }

    public Task<bool> TryWriteCommandAsync(WorldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var record = Require(command.WorldId);
        if (!record.SeenCommandIds.Add(command.CommandId))
        {
            return Task.FromResult(false);
        }

        record.Pending[command.CommandId] = command;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<WorldCommand>> ReadPendingCommandsAsync(WorldId worldId, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldCommand> pending =
        [
            .. Require(worldId).Pending.Values.OrderBy(c => c.CommandId, StringComparer.Ordinal).Take(limit)
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
            record.Pending.Remove(id);
        }

        return Task.CompletedTask;
    }

    private Record Require(WorldId worldId) =>
        _worlds.TryGetValue(worldId.Value, out var record)
            ? record
            : throw new InvalidOperationException($"World '{worldId.Value}' does not exist.");

    private sealed class Lease(WorldId worldId, Action? release) : IWorldLease
    {
        public WorldId WorldId { get; } = worldId;

        public ValueTask DisposeAsync()
        {
            release?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Deterministic minute-bucket schedule for application tests.</summary>
public sealed class TestSchedule : IWorldSchedule
{
    private readonly Dictionary<DateTimeOffset, SortedSet<string>> _buckets = [];

    public DateTimeOffset? Watermark { get; private set; }

    public Task MarkDueAsync(WorldId worldId, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bucket = TickRunner.Bucket(instant);
        if (!_buckets.TryGetValue(bucket, out var worlds))
        {
            worlds = new SortedSet<string>(StringComparer.Ordinal);
            _buckets[bucket] = worlds;
        }

        worlds.Add(worldId.Value);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorldId>> ReadBucketAsync(DateTimeOffset bucket, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldId> worlds = _buckets.TryGetValue(TickRunner.Bucket(bucket), out var set)
            ? [.. set.Take(limit).Select(WorldId.Parse)]
            : [];
        return Task.FromResult(worlds);
    }

    public Task<DateTimeOffset?> TryGetWatermarkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Watermark);
    }

    public Task SetWatermarkAsync(DateTimeOffset watermark, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Watermark = watermark;
        return Task.CompletedTask;
    }
}

/// <summary>A Worldmind that returns a hostile response, used to prove validation happens first.</summary>
public sealed class HostileGameMaster(string candidateId, string narration) : IGameMaster
{
    public string Name => "HostileGameMaster";

    public int Calls { get; private set; }

    public Task<GameMasterResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new GameMasterResponse
        {
            Decision = new GameMasterDecision
            {
                SelectedCandidateId = candidateId,
                Ranking = [],
                Narration = narration,
                Rationale = string.Empty,
                IsFallback = false,
            },
            Succeeded = true,
            ModelDeployment = "hostile",
        });
    }
}

/// <summary>A Worldmind that never answers, used to prove the timeout path.</summary>
public sealed class HangingGameMaster : IGameMaster
{
    public string Name => "HangingGameMaster";

    public async Task<GameMasterResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
