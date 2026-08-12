using System.Collections.Immutable;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Grid;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Serialization;

/// <summary>Column-oriented cell data. Compact and stable across versions.</summary>
public sealed record CellColumns
{
    public required int[] Biome { get; init; }

    public required int[] Elevation { get; init; }

    public required int[] TemperatureDeciC { get; init; }

    public required int[] MoisturePermille { get; init; }

    public required int[] Biomass { get; init; }

    public required int[] CarryingCapacity { get; init; }

    public required int[] Drought { get; init; }

    public required int[] Disease { get; init; }

    public required int[] Fire { get; init; }

    public required int[] Flood { get; init; }

    /// <summary>
    /// Material stocks. Nullable because a stored <c>world-snapshot/1</c> document predates them; a
    /// world loaded without them simply starts from an empty stock and refills on the next tick.
    /// </summary>
    public int[]? Timber { get; init; }

    public int[]? Stone { get; init; }

    public int[]? Fibre { get; init; }
}

public sealed record ConstructionDocument
{
    public required int CellIndex { get; init; }

    public required int Kind { get; init; }

    public required int Level { get; init; }

    public required int ConditionPermille { get; init; }
}

public sealed record PopulationDocument
{
    public required string Species { get; init; }

    public required int CellIndex { get; init; }

    public required int Population { get; init; }

    public required int Energy { get; init; }

    public required int HealthPermille { get; init; }

    public required int ColdTolerance { get; init; }

    public required int DroughtTolerance { get; init; }
}

public sealed record WardenDocument
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string ControlledSpecies { get; init; }

    public required int[] ControlledRegion { get; init; }

    public required int[] Goals { get; init; }

    public required int[] GoalWeights { get; init; }

    public required int[] Taboos { get; init; }

    public required int ActionBudget { get; init; }

    public required int BudgetRenewalPerChapter { get; init; }

    public required int ImpactCeilingPerChapter { get; init; }

    public required int ImpactUsedThisChapter { get; init; }
}

/// <summary>Persisted snapshot document with an explicit schema version.</summary>
public sealed record SnapshotDocument
{
    public const string CurrentSchema = "world-snapshot/2";

    /// <summary>The schema written before materials and constructions existed. Still readable.</summary>
    public const string LegacySchema = "world-snapshot/1";

    public required string Schema { get; init; }

    public required string WorldId { get; init; }

    public required string Name { get; init; }

    public required ulong Seed { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }

    public required int TopologyVersion { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required int Season { get; init; }

    public required int Chapter { get; init; }

    public required long ChapterStartTick { get; init; }

    public required long Version { get; init; }

    public required long Tick { get; init; }

    public required int TicksPerDay { get; init; }

    public required int DaysPerYear { get; init; }

    public required int AxialTiltDegrees { get; init; }

    public required bool IsPaused { get; init; }

    public required long LastEventSequence { get; init; }

    public required long LastSignificantTick { get; init; }

    public required CellColumns Cells { get; init; }

    public required PopulationDocument[] Populations { get; init; }

    public required WardenDocument[] Wardens { get; init; }

    /// <summary>
    /// Standing constructions. Not required, so a stored <c>world-snapshot/1</c> document still binds
    /// under <c>UnmappedMemberHandling.Disallow</c>.
    /// </summary>
    public ConstructionDocument[] Constructions { get; init; } = [];
}

/// <summary>Converts between the domain world state and its persisted document.</summary>
public static class SnapshotCodec
{
    public static SnapshotDocument ToDocument(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var count = state.Cells.Length;
        var columns = new CellColumns
        {
            Biome = new int[count],
            Elevation = new int[count],
            TemperatureDeciC = new int[count],
            MoisturePermille = new int[count],
            Biomass = new int[count],
            CarryingCapacity = new int[count],
            Drought = new int[count],
            Disease = new int[count],
            Fire = new int[count],
            Flood = new int[count],
            Timber = new int[count],
            Stone = new int[count],
            Fibre = new int[count],
        };

        for (var i = 0; i < count; i++)
        {
            var cell = state.Cells[i];
            columns.Biome[i] = (int)cell.Biome;
            columns.Elevation[i] = cell.Elevation;
            columns.TemperatureDeciC[i] = cell.TemperatureDeciC;
            columns.MoisturePermille[i] = cell.Moisture.Value;
            columns.Biomass[i] = cell.Biomass;
            columns.CarryingCapacity[i] = cell.CarryingCapacity;
            columns.Drought[i] = cell.Stress.Drought.Value;
            columns.Disease[i] = cell.Stress.Disease.Value;
            columns.Fire[i] = cell.Stress.Fire.Value;
            columns.Flood[i] = cell.Stress.Flood.Value;
            columns.Timber![i] = cell.Resources.Timber;
            columns.Stone![i] = cell.Resources.Stone;
            columns.Fibre![i] = cell.Resources.Fibre;
        }

        return new SnapshotDocument
        {
            Schema = SnapshotDocument.CurrentSchema,
            WorldId = state.Id.Value,
            Name = state.Name,
            Seed = state.Seed.Value,
            SimulationVersion = state.SimulationVersion,
            ContentVersion = state.ContentVersion,
            TopologyVersion = state.TopologyVersion,
            GridWidth = state.GridWidth,
            GridHeight = state.GridHeight,
            Season = state.Season.Value,
            Chapter = state.Chapter.Value,
            ChapterStartTick = state.ChapterStartTick,
            Version = state.Version,
            Tick = state.Tick.Value,
            TicksPerDay = state.TicksPerDay,
            DaysPerYear = state.DaysPerYear,
            AxialTiltDegrees = state.AxialTiltDegrees,
            IsPaused = state.IsPaused,
            LastEventSequence = state.LastEventSequence,
            LastSignificantTick = state.LastSignificantTick,
            Cells = columns,
            Populations = [.. state.Populations.Select(p => new PopulationDocument
            {
                Species = p.Species.Value,
                CellIndex = p.CellIndex,
                Population = p.Population,
                Energy = p.Energy,
                HealthPermille = p.Health.Value,
                ColdTolerance = p.Traits.ColdTolerance,
                DroughtTolerance = p.Traits.DroughtTolerance,
            })],
            Wardens = [.. state.Wardens.Select(ToDocument)],
            Constructions = [.. state.Constructions.Select(c => new ConstructionDocument
            {
                CellIndex = c.CellIndex,
                Kind = (int)c.Kind,
                Level = c.Level,
                ConditionPermille = c.Condition.Value,
            })],
        };
    }

    public static WorldState ToState(SnapshotDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Schema, SnapshotDocument.CurrentSchema, StringComparison.Ordinal)
            && !string.Equals(document.Schema, SnapshotDocument.LegacySchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported snapshot schema '{document.Schema}'.");
        }

        var topology = GridCache.Get(document.GridWidth, document.GridHeight);
        if (document.Cells.Biome.Length != topology.CellCount)
        {
            throw new InvalidOperationException("Snapshot cell count does not match the grid.");
        }

        var cells = ImmutableArray.CreateBuilder<PlanetCell>(topology.CellCount);
        for (var i = 0; i < topology.CellCount; i++)
        {
            cells.Add(new PlanetCell(
                i,
                (Biome)document.Cells.Biome[i],
                document.Cells.Elevation[i],
                document.Cells.TemperatureDeciC[i],
                new Permille(document.Cells.MoisturePermille[i]),
                document.Cells.Biomass[i],
                document.Cells.CarryingCapacity[i],
                new CellStress(
                    new Permille(document.Cells.Drought[i]),
                    new Permille(document.Cells.Disease[i]),
                    new Permille(document.Cells.Fire[i]),
                    new Permille(document.Cells.Flood[i])),
                new CellResources(
                    At(document.Cells.Timber, i),
                    At(document.Cells.Stone, i),
                    At(document.Cells.Fibre, i))));
        }

        return new WorldState
        {
            Id = WorldId.Parse(document.WorldId),
            Name = document.Name,
            Seed = new WorldSeed(document.Seed),
            SimulationVersion = document.SimulationVersion,
            ContentVersion = document.ContentVersion,
            TopologyVersion = document.TopologyVersion,
            GridWidth = document.GridWidth,
            GridHeight = document.GridHeight,
            Season = new SeasonId(document.Season),
            Chapter = new ChapterId(document.Chapter),
            ChapterStartTick = document.ChapterStartTick,
            Version = document.Version,
            Tick = new TickNumber(document.Tick),
            TicksPerDay = document.TicksPerDay,
            DaysPerYear = document.DaysPerYear,
            AxialTiltDegrees = document.AxialTiltDegrees,
            IsPaused = document.IsPaused,
            Cells = cells.ToImmutable(),
            Populations =
            [
                .. document.Populations.Select(p => new SpeciesPopulation(
                    new SpeciesId(p.Species),
                    p.CellIndex,
                    p.Population,
                    p.Energy,
                    new Permille(p.HealthPermille),
                    new AdaptationTraits(p.ColdTolerance, p.DroughtTolerance)))
            ],
            Wardens = [.. document.Wardens.Select(ToCharter)],
            Constructions =
            [
                .. document.Constructions
                    .Where(c => c.CellIndex >= 0 && c.CellIndex < topology.CellCount)
                    .OrderBy(c => c.CellIndex)
                    .Select(c => new Construction(
                        c.CellIndex,
                        (ConstructionKind)c.Kind,
                        c.Level,
                        new Permille(c.ConditionPermille)))
            ],
            LastEventSequence = document.LastEventSequence,
            LastSignificantTick = document.LastSignificantTick,
        };
    }

    /// <summary>Reads an optional column, treating a missing or short one as zero.</summary>
    private static int At(int[]? column, int index) =>
        column is not null && index < column.Length ? column[index] : 0;

    public static WardenDocument ToDocument(WardenCharter charter)
    {
        ArgumentNullException.ThrowIfNull(charter);
        return new WardenDocument
        {
            Id = charter.Id.Value,
            DisplayName = charter.DisplayName,
            ControlledSpecies = charter.ControlledSpecies.Value,
            ControlledRegion = [.. charter.ControlledRegion],
            Goals = [.. charter.Goals.Select(g => (int)g)],
            GoalWeights = [.. charter.GoalWeights],
            Taboos = [.. charter.Taboos.Select(t => (int)t)],
            ActionBudget = charter.ActionBudget,
            BudgetRenewalPerChapter = charter.BudgetRenewalPerChapter,
            ImpactCeilingPerChapter = charter.ImpactCeilingPerChapter,
            ImpactUsedThisChapter = charter.ImpactUsedThisChapter,
        };
    }

    public static WardenCharter ToCharter(WardenDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new WardenCharter
        {
            Id = WardenId.Parse(document.Id),
            DisplayName = document.DisplayName,
            ControlledSpecies = new SpeciesId(document.ControlledSpecies),
            ControlledRegion = [.. document.ControlledRegion],
            Goals = [.. document.Goals.Select(g => (WardenGoal)g)],
            GoalWeights = [.. document.GoalWeights],
            Taboos = [.. document.Taboos.Select(t => (WardenTaboo)t)],
            ActionBudget = document.ActionBudget,
            BudgetRenewalPerChapter = document.BudgetRenewalPerChapter,
            ImpactCeilingPerChapter = document.ImpactCeilingPerChapter,
            ImpactUsedThisChapter = document.ImpactUsedThisChapter,
        };
    }
}
