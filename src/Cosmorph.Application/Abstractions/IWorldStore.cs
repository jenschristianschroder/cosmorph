using Cosmorph.Application.Worlds;
using Cosmorph.Application.Worldmind;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Abstractions;

/// <summary>A manifest together with the concurrency token required to replace it.</summary>
public readonly record struct ManifestWithToken(WorldManifest Manifest, string ConcurrencyToken);

/// <summary>
/// Durable, world-scoped persistence. Every operation is scoped by <see cref="WorldId"/>; cross-world
/// reads and writes are impossible through this interface.
/// </summary>
public interface IWorldStore
{
    Task<ManifestWithToken?> TryGetManifestAsync(WorldId worldId, CancellationToken cancellationToken);

    /// <summary>Lists public worlds only, so private world existence is never disclosed.</summary>
    Task<IReadOnlyList<WorldManifest>> ListPublicWorldsAsync(CancellationToken cancellationToken);

    Task<WorldState?> TryLoadSnapshotAsync(WorldId worldId, long tick, CancellationToken cancellationToken);

    Task WriteSnapshotAsync(WorldState state, CancellationToken cancellationToken);

    /// <summary>Appends events, ignoring any whose sequence was already committed.</summary>
    Task AppendEventsAsync(WorldId worldId, IReadOnlyList<WorldEvent> events, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorldEvent>> ReadEventsAsync(WorldId worldId, long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>Creates a world. Returns false when the world already exists.</summary>
    Task<bool> TryCreateAsync(WorldState state, WorldManifest manifest, CancellationToken cancellationToken);

    /// <summary>Replaces the manifest only when the concurrency token still matches.</summary>
    Task<bool> TryReplaceManifestAsync(WorldManifest manifest, string concurrencyToken, CancellationToken cancellationToken);

    /// <summary>Acquires a short exclusive lease for advancing one world, or returns null.</summary>
    Task<IWorldLease?> TryAcquireLeaseAsync(WorldId worldId, TimeSpan duration, CancellationToken cancellationToken);

    Task WriteDecisionAuditAsync(DecisionAuditRecord record, CancellationToken cancellationToken);

    /// <summary>Returns a previously accepted decision for the same request fingerprint, if any.</summary>
    Task<DecisionAuditRecord?> TryGetDecisionAsync(WorldId worldId, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Stores a validated command. Returns false when the idempotency key was already used.</summary>
    Task<bool> TryWriteCommandAsync(WorldCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorldCommand>> ReadPendingCommandsAsync(WorldId worldId, int limit, CancellationToken cancellationToken);

    Task MarkCommandsAppliedAsync(WorldId worldId, IReadOnlyList<string> commandIds, CancellationToken cancellationToken);
}

/// <summary>An exclusive, time-bounded lease over one world.</summary>
public interface IWorldLease : IAsyncDisposable
{
    WorldId WorldId { get; }
}
