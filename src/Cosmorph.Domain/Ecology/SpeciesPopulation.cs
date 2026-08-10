using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>Inherited, bounded adaptation traits of a population.</summary>
public readonly record struct AdaptationTraits(int ColdTolerance, int DroughtTolerance)
{
    public const int MinTrait = 0;
    public const int MaxTrait = 1000;

    public static AdaptationTraits Baseline { get; } = new(300, 300);

    public AdaptationTraits Adjust(int cold, int drought) => new(
        Math.Clamp(ColdTolerance + cold, MinTrait, MaxTrait),
        Math.Clamp(DroughtTolerance + drought, MinTrait, MaxTrait));
}

/// <summary>A population of one species inside one cell.</summary>
public readonly record struct SpeciesPopulation(
    SpeciesId Species,
    int CellIndex,
    int Population,
    int Energy,
    Permille Health,
    AdaptationTraits Traits)
{
    public const int MaxPopulation = 10_000_000;

    public SpeciesPopulation WithPopulation(int population) =>
        this with { Population = Math.Clamp(population, 0, MaxPopulation) };
}
