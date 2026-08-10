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
}

/// <summary>Versioned spectator snapshot polled by the Observatory.</summary>
public sealed record SpectatorSnapshotDto
{
    public const string CurrentSchema = "spectator-snapshot/1";

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
        };

        var pressure = new long[count];
        foreach (var population in state.Populations)
        {
            pressure[population.CellIndex] += population.Population;
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

    public static SpectatorEventDto ToEvent(WorldEvent worldEvent, int gridWidth, int gridHeight)
    {
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
