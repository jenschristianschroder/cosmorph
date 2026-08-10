using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Content;

/// <summary>
/// Immutable, version-addressed species definition. Content is data-driven but not authorable at runtime.
/// </summary>
public sealed record SpeciesDefinition(
    SpeciesId Id,
    string DisplayName,
    SpeciesArchetype Archetype,
    int PreferredTemperatureDeciC,
    int TemperatureToleranceDeciC,
    int MinimumMoisturePermille,
    int BiomassPerIndividual,
    int ReproductionPermille,
    int MortalityPermille,
    int StarvationMortalityPermille,
    IReadOnlyList<Biome> Habitats)
{
    public bool CanLiveIn(Biome biome) => Habitats.Contains(biome);
}
