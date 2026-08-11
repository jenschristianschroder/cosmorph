using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worlds;

/// <summary>
/// Small, frequently replaced document describing the durable position of one world.
/// It is the only mutable blob in a world's layout.
/// </summary>
public sealed record WorldManifest
{
    public const string SchemaVersion = "world-manifest/1";

    public required WorldId Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Actor who created the world, and the only one who may reconfigure it. Optional rather than
    /// required so manifests written before ownership existed still load; such a world is one nobody
    /// may reconfigure. Never projected into a spectator response.
    /// </summary>
    public string? OwnerId { get; init; }

    public required ulong Seed { get; init; }

    public required long Tick { get; init; }

    public required long Version { get; init; }

    public required int Chapter { get; init; }

    public required long LastEventSequence { get; init; }

    /// <summary>Logical time the world has been advanced to. Never the worker's wake-up time.</summary>
    public required DateTimeOffset LastAdvancedAtUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required bool IsPaused { get; init; }

    /// <summary>Only public worlds may be listed or read anonymously.</summary>
    public required bool IsPublic { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }
}
