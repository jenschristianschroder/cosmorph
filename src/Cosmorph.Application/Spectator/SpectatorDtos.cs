using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Spectator;

/// <summary>Public world summary. Contains no Warden configuration, identities or storage details.</summary>
public sealed record WorldSummaryDto
{
    public required string WorldId { get; init; }

    public required string Name { get; init; }

    public required long Tick { get; init; }

    public required long Day { get; init; }

    public required int Season { get; init; }

    public required int Chapter { get; init; }

    public required string SeasonPhase { get; init; }

    public required int HealthPermille { get; init; }

    public required bool IsPaused { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }

    /// <summary>Which Worldmind is configured. Surfaced so local fake-AI mode is unmistakable.</summary>
    public required string WorldmindMode { get; init; }
}

/// <summary>Column-oriented spectator cell data, sized for a GPU texture upload.</summary>
public sealed record SpectatorCells
{
    public required int[] Biome { get; init; }

    public required int[] VitalityPermille { get; init; }

    public required int[] DominantStress { get; init; }

    public required int[] StressPermille { get; init; }

    public required int[] TemperatureDeciC { get; init; }

    public required int[] MoisturePermille { get; init; }

    public required int[] PopulationPressurePermille { get; init; }

    /// <summary>Combined material stock of the cell, as a share of the per-cell ceiling.</summary>
    public required int[] ResourceRichnessPermille { get; init; }

    /// <summary>The kind of construction standing on the cell, or zero for none.</summary>
    public required int[] ConstructionKind { get; init; }
}

/// <summary>Versioned spectator snapshot polled by the Observatory.</summary>
public sealed record SpectatorSnapshotDto
{
    public const string CurrentSchema = "spectator-snapshot/2";

    public required string Schema { get; init; }

    public required string WorldId { get; init; }

    public required long Tick { get; init; }

    public required long Version { get; init; }

    public required long Day { get; init; }

    public required int Season { get; init; }

    public required int Chapter { get; init; }

    public required int HealthPermille { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required long LastEventSequence { get; init; }

    public required SpeciesTotalDto[] Species { get; init; }

    public required SpectatorCells Cells { get; init; }
}

public sealed record SpeciesTotalDto
{
    public required string Species { get; init; }

    public required string DisplayName { get; init; }

    public required string Archetype { get; init; }

    public required long Population { get; init; }
}

/// <summary>One species living in a single cell, with its local condition.</summary>
public sealed record SpeciesAtCellDto
{
    public required string Species { get; init; }

    public required string DisplayName { get; init; }

    public required string Archetype { get; init; }

    public required int Population { get; init; }

    public required int HealthPermille { get; init; }

    public required int Energy { get; init; }

    public required int ColdTolerance { get; init; }

    public required int DroughtTolerance { get; init; }
}

/// <summary>The construction standing on a cell.</summary>
public sealed record ConstructionDto
{
    public required string Kind { get; init; }

    public required int Level { get; init; }

    public required int ConditionPermille { get; init; }
}

/// <summary>
/// Everything a spectator may see about one place. Fetched on demand rather than folded into the
/// snapshot, which every viewer polls every few seconds. Carries no Warden configuration.
/// </summary>
public sealed record CellDetailDto
{
    public const string CurrentSchema = "spectator-cell/1";

    public required string Schema { get; init; }

    public required string WorldId { get; init; }

    public required long Tick { get; init; }

    public required long Version { get; init; }

    public required int CellIndex { get; init; }

    public required int LatitudeDegrees { get; init; }

    public required int LongitudeDegrees { get; init; }

    public required string Biome { get; init; }

    public required bool IsLand { get; init; }

    public required int Elevation { get; init; }

    public required int TemperatureDeciC { get; init; }

    public required int MoisturePermille { get; init; }

    public required int Biomass { get; init; }

    public required int CarryingCapacity { get; init; }

    public required int VitalityPermille { get; init; }

    public required string DominantStress { get; init; }

    public required int DroughtPermille { get; init; }

    public required int DiseasePermille { get; init; }

    public required int FirePermille { get; init; }

    public required int FloodPermille { get; init; }

    public required int Timber { get; init; }

    public required int Stone { get; init; }

    public required int Fibre { get; init; }

    public required int ResourceRichnessPermille { get; init; }

    /// <summary>What the cell will add to its stock on the next tick, at the present condition.</summary>
    public required int TimberYield { get; init; }

    public required int StoneYield { get; init; }

    public required int FibreYield { get; init; }

    public ConstructionDto? Construction { get; init; }

    public required SpeciesAtCellDto[] Species { get; init; }
}

/// <summary>
/// A block of places around one centre. Carries whole <see cref="CellDetailDto"/> values rather than
/// a reduced shape, so there is one projection of a place and the browser has one parser for it.
/// </summary>
public sealed record NeighbourhoodDto
{
    public const string CurrentSchema = "spectator-neighbourhood/1";

    /// <summary>The widest block offered, which at radius 2 is 25 places.</summary>
    public const int MaxRadius = 2;

    public required string Schema { get; init; }

    public required string WorldId { get; init; }

    public required long Tick { get; init; }

    public required long Version { get; init; }

    public required int CenterCellIndex { get; init; }

    public required int Radius { get; init; }

    /// <summary>Rows in the block, which is fewer than the radius allows near a pole.</summary>
    public required int Rows { get; init; }

    public required int Columns { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    /// <summary>Row-major over the block, so index <c>r * Columns + c</c> is the cell at that spot.</summary>
    public required CellDetailDto[] Cells { get; init; }
}

/// <summary>One species and where it lives, as a column over every cell of the grid.</summary>
public sealed record SpeciesColumnDto
{
    public required string Species { get; init; }

    public required string DisplayName { get; init; }

    public required string Archetype { get; init; }

    /// <summary>Individuals in each cell, indexed by cell. Mostly zero: nothing lives at sea.</summary>
    public required int[] Population { get; init; }
}

/// <summary>
/// What is alive on every cell of the world, and what the ground is made of. Read separately from the
/// snapshot rather than folded into it, because only a viewer zoomed in far enough to see individual
/// creatures needs it and every viewer polls the snapshot. Carries no Warden configuration.
/// </summary>
/// <remarks>
/// Deliberately carries nothing the snapshot already has. Biome, temperature, moisture, stress and
/// constructions all reach the close-up from <see cref="SpectatorCells"/>, so neither read restates
/// the other and the two can never disagree about the same cell.
/// </remarks>
public sealed record WorldLifeDto
{
    public const string CurrentSchema = "spectator-life/1";

    public required string Schema { get; init; }

    public required string WorldId { get; init; }

    public required long Tick { get; init; }

    public required long Version { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    /// <summary>Height of each cell, 0 to 1000, with sea level at <see cref="SeaLevel"/>.</summary>
    public required int[] Elevation { get; init; }

    /// <summary>How full each cell is of living matter, in permille of its carrying capacity.</summary>
    public required int[] BiomassPermille { get; init; }

    public required int[] Timber { get; init; }

    public required int[] Stone { get; init; }

    public required int[] Fibre { get; init; }

    /// <summary>Ordered exactly like <see cref="SpectatorSnapshotDto.Species"/>.</summary>
    public required SpeciesColumnDto[] Species { get; init; }

    /// <summary>
    /// The elevation at which land begins, mirroring <see cref="WorldGenerator.SeaLevel"/>. Sent so
    /// the browser draws the coastline where the generator put it rather than where it guessed.
    /// </summary>
    public required int SeaLevel { get; init; }
}

/// <summary>A Chronicle entry as shown to spectators.</summary>
public sealed record SpectatorEventDto
{
    public required long Sequence { get; init; }

    public required long Tick { get; init; }

    public required string Type { get; init; }

    public required int Chapter { get; init; }

    public int? CellIndex { get; init; }

    public int? LatitudeDegrees { get; init; }

    public int? LongitudeDegrees { get; init; }

    public string? Species { get; init; }

    public required int Magnitude { get; init; }

    public string? Narration { get; init; }
}

/// <summary>Builds spectator read models. Private configuration is never projected.</summary>
public static class SpectatorMapper
{
    public static WorldSummaryDto ToSummary(WorldState state, DateTimeOffset lastUpdatedUtc, string worldmindMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        var instant = state.Instant;
        return new WorldSummaryDto
        {
            WorldId = state.Id.Value,
            Name = state.Name,
            Tick = state.Tick.Value,
            Day = instant.Day,
            Season = state.Season.Value,
            Chapter = state.Chapter.Value,
            SeasonPhase = SeasonPhase(instant.DayOfYear(state.DaysPerYear), state.DaysPerYear),
            HealthPermille = state.Health.Value,
            IsPaused = state.IsPaused,
            LastUpdatedUtc = lastUpdatedUtc,
            GridWidth = state.GridWidth,
            GridHeight = state.GridHeight,
            SimulationVersion = state.SimulationVersion,
            ContentVersion = state.ContentVersion,
            WorldmindMode = worldmindMode,
        };
    }

    public static SpectatorSnapshotDto ToSnapshot(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var count = state.Cells.Length;
        var cells = new SpectatorCells
        {
            Biome = new int[count],
            VitalityPermille = new int[count],
            DominantStress = new int[count],
            StressPermille = new int[count],
            TemperatureDeciC = new int[count],
            MoisturePermille = new int[count],
            PopulationPressurePermille = new int[count],
            ResourceRichnessPermille = new int[count],
            ConstructionKind = new int[count],
        };

        var pressure = new long[count];
        foreach (var population in state.Populations)
        {
            pressure[population.CellIndex] += population.Population;
        }

        foreach (var construction in state.Constructions)
        {
            if (construction.CellIndex >= 0 && construction.CellIndex < count)
            {
                cells.ConstructionKind[construction.CellIndex] = (int)construction.Kind;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var cell = state.Cells[i];
            var dominant = cell.Stress.Dominant;
            cells.Biome[i] = (int)cell.Biome;
            cells.VitalityPermille[i] = cell.Vitality.Value;
            cells.DominantStress[i] = (int)dominant;
            cells.StressPermille[i] = dominant switch
            {
                StressKind.Drought => cell.Stress.Drought.Value,
                StressKind.Disease => cell.Stress.Disease.Value,
                StressKind.Fire => cell.Stress.Fire.Value,
                StressKind.Flood => cell.Stress.Flood.Value,
                _ => 0,
            };
            cells.TemperatureDeciC[i] = cell.TemperatureDeciC;
            cells.MoisturePermille[i] = cell.Moisture.Value;
            var ceiling = Math.Max(1, cell.CarryingCapacity);
            cells.PopulationPressurePermille[i] = (int)Math.Clamp(pressure[i] * 1000 / ceiling, 0, 1000);
            cells.ResourceRichnessPermille[i] = cell.Resources.Richness.Value;
        }

        var content = state.Content;
        var totals = state.Populations
            .GroupBy(p => p.Species.Value, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var definition = content[new SpeciesId(g.Key)];
                return new SpeciesTotalDto
                {
                    Species = g.Key,
                    DisplayName = definition.DisplayName,
                    Archetype = definition.Archetype.ToString(),
                    Population = g.Sum(p => (long)p.Population),
                };
            })
            .ToArray();

        return new SpectatorSnapshotDto
        {
            Schema = SpectatorSnapshotDto.CurrentSchema,
            WorldId = state.Id.Value,
            Tick = state.Tick.Value,
            Version = state.Version,
            Day = state.Instant.Day,
            Season = state.Season.Value,
            Chapter = state.Chapter.Value,
            HealthPermille = state.Health.Value,
            GridWidth = state.GridWidth,
            GridHeight = state.GridHeight,
            LastEventSequence = state.LastEventSequence,
            Species = totals,
            Cells = cells,
        };
    }

    /// <summary>
    /// Projects one place. Returns null for an index outside the grid, so the endpoint can answer with
    /// the same not-found shape it uses for an unknown world.
    /// </summary>
    public static CellDetailDto? ToCellDetail(WorldState state, int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellIndex < 0 || cellIndex >= state.Cells.Length)
        {
            return null;
        }

        var cell = state.Cells[cellIndex];
        var topology = Domain.Grid.GridCache.Get(state.GridWidth, state.GridHeight);
        var content = state.Content;
        var yield = CellResources.YieldPerTick(cell);

        var standing = state.Constructions.FirstOrDefault(c => c.CellIndex == cellIndex);
        var construction = standing.IsStanding
            ? new ConstructionDto
            {
                Kind = standing.Kind.ToString(),
                Level = standing.Level,
                ConditionPermille = standing.Condition.Value,
            }
            : null;

        var species = state.Populations
            .Where(p => p.CellIndex == cellIndex)
            .OrderBy(p => p.Species.Value, StringComparer.Ordinal)
            .Select(p =>
            {
                var definition = content[p.Species];
                return new SpeciesAtCellDto
                {
                    Species = p.Species.Value,
                    DisplayName = definition.DisplayName,
                    Archetype = definition.Archetype.ToString(),
                    Population = p.Population,
                    HealthPermille = p.Health.Value,
                    Energy = p.Energy,
                    ColdTolerance = p.Traits.ColdTolerance,
                    DroughtTolerance = p.Traits.DroughtTolerance,
                };
            })
            .ToArray();

        return new CellDetailDto
        {
            Schema = CellDetailDto.CurrentSchema,
            WorldId = state.Id.Value,
            Tick = state.Tick.Value,
            Version = state.Version,
            CellIndex = cellIndex,
            LatitudeDegrees = topology.LatitudeDegrees(cellIndex),
            LongitudeDegrees = topology.LongitudeDegrees(cellIndex),
            Biome = cell.Biome.ToString(),
            IsLand = cell.IsLand,
            Elevation = cell.Elevation,
            TemperatureDeciC = cell.TemperatureDeciC,
            MoisturePermille = cell.Moisture.Value,
            Biomass = cell.Biomass,
            CarryingCapacity = cell.CarryingCapacity,
            VitalityPermille = cell.Vitality.Value,
            DominantStress = cell.Stress.Dominant.ToString(),
            DroughtPermille = cell.Stress.Drought.Value,
            DiseasePermille = cell.Stress.Disease.Value,
            FirePermille = cell.Stress.Fire.Value,
            FloodPermille = cell.Stress.Flood.Value,
            Timber = cell.Resources.Timber,
            Stone = cell.Resources.Stone,
            Fibre = cell.Resources.Fibre,
            ResourceRichnessPermille = cell.Resources.Richness.Value,
            TimberYield = yield.Timber,
            StoneYield = yield.Stone,
            FibreYield = yield.Fibre,
            Construction = construction,
            Species = species,
        };
    }

    /// <summary>
    /// Projects the block of places around a centre. Columns wrap across the date line because the
    /// grid is a cylinder; rows are clipped at the poles because it is not a torus. Returns null for a
    /// centre outside the grid, so the endpoint answers it exactly like an unknown world.
    /// </summary>
    public static NeighbourhoodDto? ToNeighbourhood(WorldState state, int cellIndex, int radius)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellIndex < 0 || cellIndex >= state.Cells.Length || radius < 0 || radius > NeighbourhoodDto.MaxRadius)
        {
            return null;
        }

        var width = state.GridWidth;
        var height = state.GridHeight;
        var centerRow = cellIndex / width;
        var centerColumn = cellIndex % width;

        var firstRow = Math.Max(0, centerRow - radius);
        var lastRow = Math.Min(height - 1, centerRow + radius);
        var rows = lastRow - firstRow + 1;

        // A grid narrower than the block would otherwise list the same column twice.
        var columns = Math.Min(width, (radius * 2) + 1);
        var firstColumn = centerColumn - radius;

        var cells = new CellDetailDto[rows * columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var column = (((firstColumn + c) % width) + width) % width;
                var index = ((firstRow + r) * width) + column;

                // Non-null by construction: the row is clipped and the column wrapped into the grid.
                cells[(r * columns) + c] = ToCellDetail(state, index)!;
            }
        }

        return new NeighbourhoodDto
        {
            Schema = NeighbourhoodDto.CurrentSchema,
            WorldId = state.Id.Value,
            Tick = state.Tick.Value,
            Version = state.Version,
            CenterCellIndex = cellIndex,
            Radius = radius,
            Rows = rows,
            Columns = columns,
            GridWidth = width,
            GridHeight = height,
            Cells = cells,
        };
    }

    /// <summary>
    /// Projects what is alive on every cell, for a viewer close enough to see individual creatures.
    /// Built in a single pass over the populations rather than by asking each cell what lives on it,
    /// which is what <see cref="ToCellDetail"/> does and what would make this quadratic.
    /// </summary>
    public static WorldLifeDto ToWorldLife(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var count = state.Cells.Length;

        var elevation = new int[count];
        var biomass = new int[count];
        var timber = new int[count];
        var stone = new int[count];
        var fibre = new int[count];

        for (var i = 0; i < count; i++)
        {
            var cell = state.Cells[i];
            elevation[i] = cell.Elevation;
            biomass[i] = cell.CarryingCapacity <= 0
                ? 0
                : (int)Math.Clamp((long)cell.Biomass * 1000 / cell.CarryingCapacity, 0, 1000);
            timber[i] = cell.Resources.Timber;
            stone[i] = cell.Resources.Stone;
            fibre[i] = cell.Resources.Fibre;
        }

        // Ordered like ToSnapshot's totals, so the browser can key an appearance off the same species
        // identifier in both reads. Columns are found by identifier while the single pass fills them.
        var content = state.Content;
        var species = state.Populations
            .Select(p => p.Species.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                var definition = content[new SpeciesId(id)];
                return new SpeciesColumnDto
                {
                    Species = id,
                    DisplayName = definition.DisplayName,
                    Archetype = definition.Archetype.ToString(),
                    Population = new int[count],
                };
            })
            .ToArray();

        var columns = species.ToDictionary(s => s.Species, s => s.Population, StringComparer.Ordinal);
        foreach (var population in state.Populations)
        {
            if (population.CellIndex >= 0
                && population.CellIndex < count
                && columns.TryGetValue(population.Species.Value, out var column))
            {
                column[population.CellIndex] += population.Population;
            }
        }

        return new WorldLifeDto
        {
            Schema = WorldLifeDto.CurrentSchema,
            WorldId = state.Id.Value,
            Tick = state.Tick.Value,
            Version = state.Version,
            GridWidth = state.GridWidth,
            GridHeight = state.GridHeight,
            Elevation = elevation,
            BiomassPermille = biomass,
            Timber = timber,
            Stone = stone,
            Fibre = fibre,
            Species = species,
            SeaLevel = WorldGenerator.SeaLevel,
        };
    }

    public static SpectatorEventDto ToEvent(WorldEvent worldEvent, int gridWidth, int gridHeight)    {
        ArgumentNullException.ThrowIfNull(worldEvent);
        int? latitude = null;
        int? longitude = null;
        if (worldEvent.CellIndex is { } index && index >= 0 && index < gridWidth * gridHeight)
        {
            var topology = Domain.Grid.GridCache.Get(gridWidth, gridHeight);
            latitude = topology.LatitudeDegrees(index);
            longitude = topology.LongitudeDegrees(index);
        }

        return new SpectatorEventDto
        {
            Sequence = worldEvent.Sequence,
            Tick = worldEvent.Tick,
            Type = worldEvent.Type.ToString(),
            Chapter = worldEvent.Chapter.Value,
            CellIndex = worldEvent.CellIndex,
            LatitudeDegrees = latitude,
            LongitudeDegrees = longitude,
            Species = worldEvent.Species?.Value,
            Magnitude = worldEvent.Magnitude,
            Narration = worldEvent.Narration,
        };
    }

    private static string SeasonPhase(int dayOfYear, int daysPerYear)
    {
        if (daysPerYear <= 0)
        {
            return "Unknown";
        }

        var quarter = dayOfYear * 4 / daysPerYear;
        return quarter switch
        {
            0 => "Thaw",
            1 => "High Sun",
            2 => "Fall",
            _ => "Long Dark",
        };
    }
}
