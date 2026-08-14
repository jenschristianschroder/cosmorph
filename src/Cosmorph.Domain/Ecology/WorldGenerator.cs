using System.Collections.Immutable;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Grid;
using Cosmorph.Domain.Random;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>Creates a world deterministically from its seed. The same seed always produces the same planet.</summary>
public static class WorldGenerator
{
    public const int DefaultTicksPerDay = 1;
    public const int DefaultDaysPerYear = 96;
    public const int DefaultAxialTiltDegrees = 23;

    /// <summary>The highest elevation still under water. Elevation itself runs from 0 to 1000.</summary>
    public const int SeaLevel = 520;

    public const int MaxElevation = 1000;

    public static WorldState Create(
        WorldId id,
        string name,
        WorldSeed seed,
        int width = 64,
        int height = 32,
        int ticksPerDay = DefaultTicksPerDay,
        int daysPerYear = DefaultDaysPerYear,
        int axialTiltDegrees = DefaultAxialTiltDegrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, 60);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticksPerDay, 1440);
        ArgumentOutOfRangeException.ThrowIfLessThan(daysPerYear, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(daysPerYear, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(axialTiltDegrees);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(axialTiltDegrees, 60);

        var topology = GridCache.Get(width, height);
        var cells = ImmutableArray.CreateBuilder<PlanetCell>(topology.CellCount);
        for (var index = 0; index < topology.CellCount; index++)
        {
            cells.Add(CreateCell(seed, topology, index, daysPerYear, axialTiltDegrees));
        }

        var content = ContentPack.Season1;
        var populations = CreatePopulations(seed, cells, content);

        return new WorldState
        {
            Id = id,
            Name = name,
            Seed = seed,
            SimulationVersion = WorldState.CurrentSimulationVersion,
            ContentVersion = content.Version,
            TopologyVersion = topology.Version,
            GridWidth = width,
            GridHeight = height,
            Season = new SeasonId(1),
            Chapter = new ChapterId(1),
            ChapterStartTick = 0,
            Version = 1,
            Tick = new TickNumber(0),
            TicksPerDay = ticksPerDay,
            DaysPerYear = daysPerYear,
            AxialTiltDegrees = axialTiltDegrees,
            IsPaused = false,
            Cells = cells.ToImmutable(),
            Populations = populations,
            Wardens = [],
            LastEventSequence = 0,
        };
    }

    /// <summary>Value noise on a wrapping lattice, returned in permille.</summary>
    internal static int Noise(WorldSeed seed, ICellTopology topology, int index, int lattice, long salt)
    {
        var (x, y) = topology.CoordinatesOf(index);
        var cellX = x * lattice / topology.Width;
        var cellY = y * lattice / topology.Height;
        var fracX = ((x * lattice) % topology.Width) * 1000 / topology.Width;
        var fracY = ((y * lattice) % topology.Height) * 1000 / topology.Height;

        var c00 = Corner(seed, cellX, cellY, lattice, salt);
        var c10 = Corner(seed, cellX + 1, cellY, lattice, salt);
        var c01 = Corner(seed, cellX, cellY + 1, lattice, salt);
        var c11 = Corner(seed, cellX + 1, cellY + 1, lattice, salt);

        var top = c00 + ((c10 - c00) * Smooth(fracX) / 1000);
        var bottom = c01 + ((c11 - c01) * Smooth(fracX) / 1000);
        return Math.Clamp(top + ((bottom - top) * Smooth(fracY) / 1000), 0, 1000);
    }

    private static int Smooth(int t) => t * t * ((3000 - (2 * t)) / 1000) / 1000;

    private static int Corner(WorldSeed seed, int cellX, int cellY, int lattice, long salt) =>
        DeterministicRandom.NextPermille(seed.Value, ((cellX % lattice) + lattice) % lattice, cellY, lattice, salt);

    private static PlanetCell CreateCell(
        WorldSeed seed,
        ICellTopology topology,
        int index,
        int daysPerYear,
        int axialTiltDegrees)
    {
        var latitude = topology.LatitudeDegrees(index);
        var continent = (Noise(seed, topology, index, 4, 0x101) * 6
            + Noise(seed, topology, index, 9, 0x202) * 3
            + Noise(seed, topology, index, 17, 0x303)) / 10;

        // Poles are colder and lower; taper the continents slightly toward the poles.
        var elevation = Math.Clamp(continent - (Math.Abs(latitude) / 12), 0, MaxElevation);
        var isLand = elevation >= SeaLevel;
        var humidity = Noise(seed, topology, index, 6, 0x404);

        var provisional = new PlanetCell(
            index,
            isLand ? Biome.Grassland : Biome.Ocean,
            elevation,
            0,
            Permille.Clamp(humidity),
            0,
            0,
            CellStress.None);

        var temperature = Climate.TemperatureDeciC(seed, topology, provisional, 0, daysPerYear, axialTiltDegrees);
        var biome = ClassifyBiome(isLand, elevation, latitude, temperature, humidity);
        var withBiome = provisional with { Biome = biome, TemperatureDeciC = temperature };
        var moisture = Climate.MoisturePermille(seed, topology, withBiome, 0, daysPerYear);
        var capacity = CarryingCapacity(biome, temperature, moisture);

        return withBiome with
        {
            Moisture = moisture,
            CarryingCapacity = capacity,
            Biomass = capacity * 6 / 10,
        };
    }

    /// <summary>Derives a biome from elevation, latitude, temperature and moisture.</summary>
    public static Biome ClassifyBiome(bool isLand, int elevation, int latitudeDegrees, int temperatureDeciC, int humidity)
    {
        if (!isLand)
        {
            return temperatureDeciC <= -60 ? Biome.Ice : Biome.Ocean;
        }

        if (elevation >= 860)
        {
            return Biome.Mountain;
        }

        if (temperatureDeciC <= -40)
        {
            return Math.Abs(latitudeDegrees) >= 75 ? Biome.Ice : Biome.Tundra;
        }

        if (temperatureDeciC <= 60)
        {
            return humidity >= 450 ? Biome.BorealForest : Biome.Tundra;
        }

        if (humidity <= 250)
        {
            return Biome.Desert;
        }

        if (humidity >= 820)
        {
            return Biome.Wetland;
        }

        if (temperatureDeciC >= 240)
        {
            return humidity >= 600 ? Biome.Rainforest : Biome.Grassland;
        }

        return humidity >= 520 ? Biome.TemperateForest : Biome.Grassland;
    }

    /// <summary>Biomass carrying capacity of a cell given biome and climate.</summary>
    public static int CarryingCapacity(Biome biome, int temperatureDeciC, Permille moisture)
    {
        var basis = biome switch
        {
            Biome.Rainforest => 9000,
            Biome.Wetland => 7000,
            Biome.TemperateForest => 6500,
            Biome.BorealForest => 4500,
            Biome.Grassland => 4000,
            Biome.Mountain => 1800,
            Biome.Tundra => 1500,
            Biome.Desert => 800,
            _ => 0,
        };

        if (basis == 0)
        {
            return 0;
        }

        var temperatureFactor = 1000 - (Math.Abs(temperatureDeciC - 180) * 2);
        var factor = Math.Clamp(temperatureFactor, 200, 1000) * Math.Max(120, moisture.Value) / 1000;
        return Math.Clamp(basis * factor / 1000, 100, PlanetCell.MaxBiomass);
    }

    private static ImmutableArray<SpeciesPopulation> CreatePopulations(
        WorldSeed seed,
        IReadOnlyList<PlanetCell> cells,
        ContentPack content)
    {
        var builder = ImmutableArray.CreateBuilder<SpeciesPopulation>();
        foreach (var definition in content.Species.OrderBy(s => s.Id.Value, StringComparer.Ordinal))
        {
            foreach (var cell in cells)
            {
                if (!cell.IsLand || !definition.CanLiveIn(cell.Biome) || cell.CarryingCapacity <= 0)
                {
                    continue;
                }

                var roll = DeterministicRandom.NextPermille(seed.Value, cell.Index, definition.Id.Value.Length, 0x5EED);
                var threshold = definition.Archetype switch
                {
                    SpeciesArchetype.Producer => 200,
                    SpeciesArchetype.Herbivore => 500,
                    _ => 780,
                };

                if (roll < threshold)
                {
                    continue;
                }

                var scale = definition.Archetype switch
                {
                    SpeciesArchetype.Producer => 40,
                    SpeciesArchetype.Herbivore => 8,
                    _ => 2,
                };

                var population = Math.Max(10, cell.CarryingCapacity * scale / 1000);
                builder.Add(new SpeciesPopulation(
                    definition.Id,
                    cell.Index,
                    population,
                    Energy: 500,
                    Health: new Permille(800),
                    Traits: AdaptationTraits.Baseline));
            }
        }

        return CanonicalOrder(builder.ToImmutable());
    }

    /// <summary>Populations are always kept in a canonical order so serialization is byte-stable.</summary>
    public static ImmutableArray<SpeciesPopulation> CanonicalOrder(ImmutableArray<SpeciesPopulation> populations) =>
        [.. populations
            .Where(p => p.Population > 0)
            .OrderBy(p => p.Species.Value, StringComparer.Ordinal)
            .ThenBy(p => p.CellIndex)];
}
