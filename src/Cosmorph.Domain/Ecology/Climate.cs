using Cosmorph.Domain.Grid;
using Cosmorph.Domain.Random;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>Seasonal climate derived from world day, axial parameters, latitude and seeded variation.</summary>
public static class Climate
{
    /// <summary>Baseline temperature of a latitude before seasonal and local variation, in deci-degrees C.</summary>
    public static int BaseTemperatureDeciC(int latitudeDegrees, int elevation)
    {
        var absLatitude = Math.Abs(latitudeDegrees);
        var latitudeFactor = Trigonometry.CosMilli(absLatitude); // 1000 at equator, 0 at pole
        var temperature = -300 + (latitudeFactor * 620 / 1000);
        return temperature - (elevation * 120 / 1000);
    }

    /// <summary>Seasonal offset caused by axial tilt, in deci-degrees C.</summary>
    public static int SeasonalOffsetDeciC(long day, int daysPerYear, int latitudeDegrees, int axialTiltDegrees)
    {
        if (daysPerYear <= 0)
        {
            return 0;
        }

        var phaseDegrees = (int)(day % daysPerYear * 360 / daysPerYear);
        var seasonal = Trigonometry.SinMilli(phaseDegrees);
        var amplitude = axialTiltDegrees * Math.Abs(Trigonometry.SinMilli(latitudeDegrees)) / 1000;
        var hemisphere = latitudeDegrees >= 0 ? 1 : -1;
        return hemisphere * seasonal * amplitude * 4 / 1000;
    }

    /// <summary>Temperature of a cell for a given day, clamped to the domain range.</summary>
    public static int TemperatureDeciC(
        WorldSeed seed,
        ICellTopology topology,
        PlanetCell cell,
        long day,
        int daysPerYear,
        int axialTiltDegrees)
    {
        var latitude = topology.LatitudeDegrees(cell.Index);
        var value = BaseTemperatureDeciC(latitude, cell.Elevation)
            + SeasonalOffsetDeciC(day, daysPerYear, latitude, axialTiltDegrees)
            + DeterministicRandom.NextSigned(seed.Value, 12, day, cell.Index, 0xC1)
            + BiomeTemperatureOffset(cell.Biome);

        return Math.Clamp(value, PlanetCell.MinTemperatureDeciC, PlanetCell.MaxTemperatureDeciC);
    }

    /// <summary>Moisture of a cell for a given day, in permille.</summary>
    public static Permille MoisturePermille(
        WorldSeed seed,
        ICellTopology topology,
        PlanetCell cell,
        long day,
        int daysPerYear)
    {
        var latitude = topology.LatitudeDegrees(cell.Index);
        var baseline = BiomeBaseMoisture(cell.Biome);
        var tropical = Math.Max(0, Trigonometry.CosMilli(Math.Abs(latitude) * 2)) / 8;
        var phaseDegrees = daysPerYear <= 0 ? 0 : (int)(day % daysPerYear * 360 / daysPerYear);
        var seasonal = Trigonometry.SinMilli(phaseDegrees + (cell.Index % 90)) / 12;
        var noise = DeterministicRandom.NextSigned(seed.Value, 40, day, cell.Index, 0xD2);
        return Permille.Clamp(baseline + tropical + seasonal + noise - (cell.Elevation / 12));
    }

    private static int BiomeTemperatureOffset(Biome biome) => biome switch
    {
        Biome.Desert => 60,
        Biome.Rainforest => 20,
        Biome.Ice => -80,
        Biome.Tundra => -40,
        Biome.Mountain => -50,
        Biome.Ocean => 10,
        _ => 0,
    };

    private static int BiomeBaseMoisture(Biome biome) => biome switch
    {
        Biome.Ocean => 900,
        Biome.Wetland => 780,
        Biome.Rainforest => 720,
        Biome.TemperateForest => 560,
        Biome.BorealForest => 480,
        Biome.Grassland => 400,
        Biome.Tundra => 340,
        Biome.Mountain => 320,
        Biome.Ice => 300,
        Biome.Desert => 120,
        _ => 300,
    };
}
