using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>
/// A single cell of the planet. All authoritative quantities are integers or fixed-point values.
/// </summary>
public readonly record struct PlanetCell(
    int Index,
    Biome Biome,
    int Elevation,
    int TemperatureDeciC,
    Permille Moisture,
    int Biomass,
    int CarryingCapacity,
    CellStress Stress)
{
    public const int MaxBiomass = 100_000;
    public const int MinTemperatureDeciC = -900;
    public const int MaxTemperatureDeciC = 600;

    public bool IsLand => Biome is not (Biome.Ocean or Biome.Ice);

    /// <summary>Ecological vitality of the cell in permille, used by the spectator view.</summary>
    public Permille Vitality
    {
        get
        {
            if (CarryingCapacity <= 0)
            {
                return Permille.Zero;
            }

            var fill = (int)Math.Min(1000L, (long)Biomass * 1000 / CarryingCapacity);
            return Permille.Clamp(fill - (Stress.Total / 4));
        }
    }

    public PlanetCell WithBiomass(int biomass) => this with { Biomass = Math.Clamp(biomass, 0, Math.Min(MaxBiomass, CarryingCapacity)) };
}
