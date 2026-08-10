using System.Collections.Immutable;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Grid;
using Cosmorph.Domain.Wardens;

namespace Cosmorph.Domain.Worlds;

/// <summary>Immutable, isolated state of exactly one world.</summary>
public sealed record WorldState
{
    public const string CurrentSimulationVersion = "sim/1.0.0";

    public required WorldId Id { get; init; }

    /// <summary>Untrusted display name. Encoded on output and never treated as an instruction.</summary>
    public required string Name { get; init; }

    public required WorldSeed Seed { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }

    public required int TopologyVersion { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required SeasonId Season { get; init; }

    public required ChapterId Chapter { get; init; }

    public required long ChapterStartTick { get; init; }

    /// <summary>Monotonic state version used for optimistic concurrency and staleness checks.</summary>
    public required long Version { get; init; }

    public required TickNumber Tick { get; init; }

    public required int TicksPerDay { get; init; }

    public required int DaysPerYear { get; init; }

    public required int AxialTiltDegrees { get; init; }

    public required bool IsPaused { get; init; }

    public required ImmutableArray<PlanetCell> Cells { get; init; }

    public required ImmutableArray<SpeciesPopulation> Populations { get; init; }

    public required ImmutableArray<WardenCharter> Wardens { get; init; }

    public required long LastEventSequence { get; init; }

    /// <summary>Tick of the most recent significant situation, used to rate-limit Worldmind calls.</summary>
    public long LastSignificantTick { get; init; }

    public WorldInstant Instant => WorldInstant.FromTick(Tick, TicksPerDay);

    public ICellTopology Topology => GridCache.Get(GridWidth, GridHeight);

    /// <summary>Ticks per chapter. A chapter closes with a snapshot and a recap event.</summary>
    public int TicksPerChapter => TicksPerDay * 30;

    public ContentPack Content => ContentPack.Season1.Version == ContentVersion
        ? ContentPack.Season1
        : throw new InvalidOperationException($"Unknown content version '{ContentVersion}'.");

    /// <summary>Overall world health in permille, derived from cell vitality.</summary>
    public Permille Health
    {
        get
        {
            var land = 0;
            long total = 0;
            foreach (var cell in Cells)
            {
                if (!cell.IsLand)
                {
                    continue;
                }

                land++;
                total += cell.Vitality.Value;
            }

            return land == 0 ? Permille.Zero : Permille.Clamp((int)(total / land));
        }
    }
}
