using Cosmorph.Application.Abstractions;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worlds;

/// <summary>Result of attempting to create a world.</summary>
public sealed record CreateWorldResult(bool Created, WorldId WorldId);

/// <summary>Creates isolated worlds from a stable seed and registers them with the scheduler.</summary>
public sealed class WorldFactory(IWorldStore store, IWorldSchedule schedule, IClock clock)
{
    public const int MaxNameLength = 60;
    public const int DefaultWidth = 64;
    public const int DefaultHeight = 32;

    private readonly IWorldStore _store = store;
    private readonly IWorldSchedule _schedule = schedule;
    private readonly IClock _clock = clock;

    /// <param name="ownerId">
    /// Actor creating the world. It is resolved from the caller's token, never from a request body,
    /// and it is the only actor permitted to reconfigure the world afterwards.
    /// </param>
    public async Task<CreateWorldResult> CreateAsync(
        WorldId worldId,
        string name,
        WorldSeed seed,
        bool isPublic,
        string ownerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException("World name is too long.", nameof(name));
        }

        var state = WorldGenerator.Create(worldId, name, seed, DefaultWidth, DefaultHeight);
        var now = _clock.UtcNow;
        var manifest = new WorldManifest
        {
            Id = worldId,
            Name = name,
            OwnerId = ownerId,
            Seed = seed.Value,
            Tick = state.Tick.Value,
            Version = state.Version,
            Chapter = state.Chapter.Value,
            LastEventSequence = state.LastEventSequence,
            LastAdvancedAtUtc = now,
            CreatedAtUtc = now,
            IsPaused = false,
            IsPublic = isPublic,
            GridWidth = state.GridWidth,
            GridHeight = state.GridHeight,
            SimulationVersion = state.SimulationVersion,
            ContentVersion = state.ContentVersion,
        };

        var created = await _store.TryCreateAsync(state, manifest, cancellationToken).ConfigureAwait(false);
        if (!created)
        {
            return new CreateWorldResult(false, worldId);
        }

        await _schedule.MarkDueAsync(worldId, now, cancellationToken).ConfigureAwait(false);
        return new CreateWorldResult(true, worldId);
    }
}
