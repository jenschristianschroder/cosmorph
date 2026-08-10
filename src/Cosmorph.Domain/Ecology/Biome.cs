namespace Cosmorph.Domain.Ecology;

/// <summary>Biome of a planet cell. Names are stable; the numeric values are part of the wire format.</summary>
public enum Biome
{
    Ocean = 0,
    Ice = 1,
    Tundra = 2,
    BorealForest = 3,
    Grassland = 4,
    TemperateForest = 5,
    Rainforest = 6,
    Desert = 7,
    Wetland = 8,
    Mountain = 9,
}
