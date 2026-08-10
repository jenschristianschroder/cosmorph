using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Content;

/// <summary>
/// Immutable, version-addressed content pack. A world records the exact content version used for
/// every event, so existing history is never rewritten by a content update.
/// </summary>
public sealed record ContentPack(string Version, IReadOnlyList<SpeciesDefinition> Species)
{
    public static ContentPack Season1 { get; } = new(
        "season-1.0.0",
        [
            new SpeciesDefinition(
                new SpeciesId("verdant-moss"),
                "Verdant Moss",
                SpeciesArchetype.Producer,
                PreferredTemperatureDeciC: 180,
                TemperatureToleranceDeciC: 260,
                MinimumMoisturePermille: 180,
                BiomassPerIndividual: 1,
                ReproductionPermille: 120,
                MortalityPermille: 40,
                StarvationMortalityPermille: 90,
                Habitats:
                [
                    Biome.Grassland, Biome.TemperateForest, Biome.Rainforest, Biome.Wetland,
                    Biome.BorealForest, Biome.Tundra, Biome.Desert, Biome.Mountain,
                ]),
            new SpeciesDefinition(
                new SpeciesId("cliff-grazer"),
                "Cliff Grazer",
                SpeciesArchetype.Herbivore,
                PreferredTemperatureDeciC: 160,
                TemperatureToleranceDeciC: 220,
                MinimumMoisturePermille: 120,
                BiomassPerIndividual: 4,
                ReproductionPermille: 70,
                MortalityPermille: 35,
                StarvationMortalityPermille: 160,
                Habitats:
                [
                    Biome.Grassland, Biome.TemperateForest, Biome.Rainforest, Biome.Wetland,
                    Biome.BorealForest, Biome.Tundra, Biome.Mountain,
                ]),
            new SpeciesDefinition(
                new SpeciesId("ember-stalker"),
                "Ember Stalker",
                SpeciesArchetype.Predator,
                PreferredTemperatureDeciC: 200,
                TemperatureToleranceDeciC: 200,
                MinimumMoisturePermille: 80,
                BiomassPerIndividual: 0,
                ReproductionPermille: 45,
                MortalityPermille: 40,
                StarvationMortalityPermille: 180,
                Habitats:
                [
                    Biome.Grassland, Biome.TemperateForest, Biome.Rainforest,
                    Biome.BorealForest, Biome.Mountain, Biome.Desert,
                ]),
        ]);

    public SpeciesDefinition this[SpeciesId id] =>
        Species.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"Unknown species '{id}'.");

    public bool TryGet(SpeciesId id, out SpeciesDefinition definition)
    {
        definition = Species.FirstOrDefault(s => s.Id == id)!;
        return definition is not null;
    }
}
