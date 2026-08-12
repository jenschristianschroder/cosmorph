using System.Collections.Immutable;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Grid;
using Cosmorph.Domain.Random;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ticking;

/// <summary>A raw ecological signal produced by a step, later turned into ordered Chronicle events.</summary>
public readonly record struct EcologySignal(WorldEventType Type, int CellIndex, SpeciesId? Species, int Magnitude);

/// <summary>
/// The deterministic ecological transition of one tick: climate, biomass, then species behaviour.
/// Contains no persistence, HTTP, serialization, logging or model calls.
/// </summary>
public static class EcologyStep
{
    public const int MigrationPermille = 120;
    public const int PopulationMilestoneStep = 10_000;

    public static (ImmutableArray<PlanetCell> Cells, ImmutableArray<SpeciesPopulation> Populations, ImmutableArray<Construction> Constructions, List<EcologySignal> Signals)
        Advance(WorldState state, long tick)
    {
        var topology = state.Topology;
        var content = state.Content;
        var day = WorldInstant.FromTick(new TickNumber(tick), state.TicksPerDay).Day;

        var cells = state.Cells.ToArray();
        var signals = new List<EcologySignal>();

        var constructions = WeatherConstructions(state, signals);
        var constructionByCell = IndexByCell(constructions, cells.Length);

        UpdateClimateAndStress(state, topology, cells, constructionByCell, day, tick, signals);

        var populations = ResolveSpecies(state, topology, content, cells, constructionByCell, tick, signals);

        return ([.. cells], populations, [.. constructions], signals);
    }

    /// <summary>
    /// Ages every construction by one tick and drops the ones that have worn away. Runs before the
    /// climate step so a structure that fails this tick no longer shelters the cell this tick.
    /// </summary>
    private static List<Construction> WeatherConstructions(WorldState state, List<EcologySignal> signals)
    {
        var standing = new List<Construction>(state.Constructions.Length);
        foreach (var construction in state.Constructions)
        {
            var weathered = construction.Weather();
            if (weathered.IsStanding)
            {
                standing.Add(weathered);
                continue;
            }

            signals.Add(new EcologySignal(
                WorldEventType.ConstructionLost,
                construction.CellIndex,
                null,
                (int)construction.Kind));
        }

        return standing;
    }

    /// <summary>Cell-index lookup into the standing constructions. A cell holds at most one.</summary>
    private static Construction?[] IndexByCell(List<Construction> constructions, int cellCount)
    {
        var byCell = new Construction?[cellCount];
        foreach (var construction in constructions)
        {
            if (construction.CellIndex >= 0 && construction.CellIndex < cellCount)
            {
                byCell[construction.CellIndex] = construction;
            }
        }

        return byCell;
    }

    private static void UpdateClimateAndStress(
        WorldState state,
        ICellTopology topology,
        PlanetCell[] cells,
        Construction?[] constructionByCell,
        long day,
        long tick,
        List<EcologySignal> signals)
    {
        for (var index = 0; index < cells.Length; index++)
        {
            var previous = cells[index];
            var temperature = Climate.TemperatureDeciC(state.Seed, topology, previous, day, state.DaysPerYear, state.AxialTiltDegrees);
            var moisture = Climate.MoisturePermille(state.Seed, topology, previous, day, state.DaysPerYear);
            var capacity = WorldGenerator.CarryingCapacity(previous.Biome, temperature, moisture);

            var construction = constructionByCell[index];
            capacity = Math.Min(PlanetCell.MaxBiomass, capacity + (capacity * (construction?.CapacityBonusPermille ?? 0) / 1000));
            var weatherRelief = construction?.WeatherReliefPermille ?? 0;

            var stress = previous.Stress.Decay(25);

            if (previous.IsLand)
            {
                if (moisture.Value < 260)
                {
                    stress = stress with { Drought = stress.Drought.Add(Damp((260 - moisture.Value) / 2, weatherRelief)) };
                }

                if (moisture.Value > 860)
                {
                    stress = stress with { Flood = stress.Flood.Add(Damp((moisture.Value - 860) * 2, weatherRelief)) };
                }

                var fireRisk = temperature > 260 && moisture.Value < 240 && capacity > 0
                    && previous.Biomass * 1000 / Math.Max(1, capacity) > 550;
                if (fireRisk && DeterministicRandom.NextPermille(state.Seed.Value, tick, index, 0xF12E) > 940)
                {
                    stress = stress with { Fire = stress.Fire.Add(320 + (temperature / 8)) };
                }

                var diseaseRisk = previous.Biomass * 1000 / Math.Max(1, capacity) > 850 && moisture.Value > 600;
                if (diseaseRisk && DeterministicRandom.NextPermille(state.Seed.Value, tick, index, 0xD15E) > 955)
                {
                    stress = stress with { Disease = stress.Disease.Add(280) };
                }
            }

            var biomass = GrowBiomass(previous, temperature, moisture, capacity, stress);

            var updated = previous with
            {
                TemperatureDeciC = temperature,
                Moisture = moisture,
                CarryingCapacity = capacity,
                Stress = stress,
                Biomass = Math.Clamp(biomass, 0, Math.Min(PlanetCell.MaxBiomass, Math.Max(capacity, 1))),
            };

            updated = updated with { Resources = updated.Resources.Add(CellResources.YieldPerTick(updated)) };

            cells[index] = updated;
            EmitStressSignals(previous, updated, signals);
        }
    }

    /// <summary>Removes a permille share of an accruing stress, used by windbreaks.</summary>
    private static int Damp(int amount, int reliefPermille) =>
        amount - (amount * Math.Clamp(reliefPermille, 0, 1000) / 1000);

    private static int GrowBiomass(PlanetCell cell, int temperature, Permille moisture, int capacity, CellStress stress)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        var suitability = Math.Clamp(1000 - (Math.Abs(temperature - 180) * 2), 0, 1000)
            * Math.Clamp(moisture.Value, 0, 1000) / 1000;
        var headroom = Math.Max(0, capacity - cell.Biomass);
        var growth = (int)((long)Math.Max(cell.Biomass, capacity / 100) * suitability / 1000 * headroom / Math.Max(1, capacity));
        var damage = (int)((long)cell.Biomass * stress.Total / 8000);
        return cell.Biomass + growth - damage;
    }

    private static void EmitStressSignals(PlanetCell before, PlanetCell after, List<EcologySignal> signals)
    {
        Compare(before.Stress.Drought.Value, after.Stress.Drought.Value, WorldEventType.Drought);
        Compare(before.Stress.Fire.Value, after.Stress.Fire.Value, WorldEventType.Fire);
        Compare(before.Stress.Disease.Value, after.Stress.Disease.Value, WorldEventType.Disease);
        Compare(before.Stress.Flood.Value, after.Stress.Flood.Value, WorldEventType.Flood);

        if (before.Vitality.Value < 300 && after.Vitality.Value >= 550)
        {
            signals.Add(new EcologySignal(WorldEventType.Recovery, after.Index, null, after.Vitality.Value));
        }

        void Compare(int previous, int current, WorldEventType type)
        {
            const int Threshold = 500;
            if (previous < Threshold && current >= Threshold)
            {
                signals.Add(new EcologySignal(type, after.Index, null, current));
            }
        }
    }

    private static ImmutableArray<SpeciesPopulation> ResolveSpecies(
        WorldState state,
        ICellTopology topology,
        ContentPack content,
        PlanetCell[] cells,
        Construction?[] constructionByCell,
        long tick,
        List<EcologySignal> signals)
    {
        var byCell = new Dictionary<(string Species, int Cell), SpeciesPopulation>();
        foreach (var population in state.Populations)
        {
            byCell[(population.Species.Value, population.CellIndex)] = population;
        }

        var herbivoreByCell = new int[cells.Length];
        foreach (var population in state.Populations)
        {
            if (content[population.Species].Archetype == SpeciesArchetype.Herbivore)
            {
                herbivoreByCell[population.CellIndex] += population.Population;
            }
        }

        var result = new Dictionary<(string Species, int Cell), SpeciesPopulation>(byCell.Count);
        var totalsBefore = TotalsBySpecies(state.Populations);

        foreach (var population in state.Populations.OrderBy(p => p.Species.Value, StringComparer.Ordinal).ThenBy(p => p.CellIndex))
        {
            var definition = content[population.Species];
            var cell = cells[population.CellIndex];
            var next = population;

            var temperatureStrain = Math.Max(
                0,
                Math.Abs(cell.TemperatureDeciC - definition.PreferredTemperatureDeciC) - definition.TemperatureToleranceDeciC);
            var coldStrain = cell.TemperatureDeciC < definition.PreferredTemperatureDeciC ? temperatureStrain : 0;
            var moistureStrain = Math.Max(0, definition.MinimumMoisturePermille - cell.Moisture.Value);

            var toleranceRelief = coldStrain > 0
                ? population.Traits.ColdTolerance / 4
                : population.Traits.DroughtTolerance / 4;
            var strain = Math.Max(0, ((temperatureStrain * 2) + moistureStrain + (cell.Stress.Total / 4)) - toleranceRelief);

            // A shelter on the cell absorbs a share of whatever strain is left.
            var shelter = constructionByCell[population.CellIndex]?.StrainReliefPermille ?? 0;
            strain -= strain * shelter / 1000;

            var starving = false;
            switch (definition.Archetype)
            {
                case SpeciesArchetype.Producer:
                    starving = cell.CarryingCapacity > 0 && cell.Biomass * 1000 / cell.CarryingCapacity < 150;
                    break;
                case SpeciesArchetype.Herbivore:
                {
                    var demand = population.Population * definition.BiomassPerIndividual;
                    var eaten = Math.Min(demand, cell.Biomass);
                    cells[population.CellIndex] = cell.WithBiomass(cell.Biomass - eaten);
                    cell = cells[population.CellIndex];
                    starving = eaten < demand * 3 / 4;
                    break;
                }

                case SpeciesArchetype.Predator:
                {
                    var prey = herbivoreByCell[population.CellIndex];
                    var demand = Math.Max(1, population.Population / 4);
                    starving = prey < demand;
                    break;
                }
            }

            var mortalityPermille = definition.MortalityPermille
                + (strain / 4)
                + (starving ? definition.StarvationMortalityPermille : 0);
            var reproductionPermille = starving
                ? 0
                : Math.Max(0, definition.ReproductionPermille - (strain / 6));

            var deaths = (int)((long)next.Population * Math.Clamp(mortalityPermille, 0, 900) / 1000);
            var births = (int)((long)next.Population * Math.Clamp(reproductionPermille, 0, 400) / 1000);
            var capacityLimit = CapacityFor(definition, cell);
            var population2 = Math.Clamp(next.Population - deaths + births, 0, capacityLimit);

            var health = next.Health.Add(starving ? -80 : 40 - (strain / 10));
            var traits = strain > 0
                ? next.Traits.Adjust(coldStrain > 0 ? 1 : 0, moistureStrain > 0 ? 1 : 0)
                : next.Traits;

            next = next with
            {
                Population = population2,
                Health = health,
                Traits = traits,
                Energy = Math.Clamp(next.Energy + (starving ? -60 : 30), 0, 1000),
            };

            if (starving && population.Population > 200)
            {
                signals.Add(new EcologySignal(
                    WorldEventType.FoodShortage,
                    population.CellIndex,
                    population.Species,
                    population.Population - next.Population));
            }

            Accumulate(result, next);

            if (starving && next.Population > 100)
            {
                var destination = ChooseMigrationTarget(topology, cells, definition, herbivoreByCell, population.CellIndex);
                if (destination >= 0)
                {
                    var movers = next.Population * MigrationPermille / 1000;
                    if (movers > 0)
                    {
                        var source = result[(next.Species.Value, next.CellIndex)];
                        result[(next.Species.Value, next.CellIndex)] =
                            source.WithPopulation(Math.Max(0, source.Population - movers));
                        Accumulate(result, next with { CellIndex = destination, Population = movers });

                        if (movers >= 100)
                        {
                            signals.Add(new EcologySignal(WorldEventType.Migration, destination, next.Species, movers));
                        }
                    }
                }
            }
        }

        var populations = WorldGenerator.CanonicalOrder([.. result.Values]);
        EmitMilestones(totalsBefore, TotalsBySpecies(populations), signals);
        return populations;
    }

    private static void Accumulate(Dictionary<(string Species, int Cell), SpeciesPopulation> result, SpeciesPopulation population)
    {
        var key = (population.Species.Value, population.CellIndex);
        result[key] = result.TryGetValue(key, out var existing)
            ? existing.WithPopulation(existing.Population + population.Population)
            : population;
    }

    private static int CapacityFor(SpeciesDefinition definition, PlanetCell cell) => definition.Archetype switch
    {
        SpeciesArchetype.Producer => Math.Max(50, cell.CarryingCapacity),
        SpeciesArchetype.Herbivore => Math.Max(20, cell.CarryingCapacity / 4),
        _ => Math.Max(5, cell.CarryingCapacity / 40),
    };

    private static int ChooseMigrationTarget(
        ICellTopology topology,
        PlanetCell[] cells,
        SpeciesDefinition definition,
        int[] herbivoreByCell,
        int fromCell)
    {
        var best = -1;
        var bestScore = int.MinValue;
        foreach (var neighbour in topology.NeighboursOf(fromCell))
        {
            var candidate = cells[neighbour];
            if (!candidate.IsLand || !definition.CanLiveIn(candidate.Biome))
            {
                continue;
            }

            var score = definition.Archetype == SpeciesArchetype.Predator
                ? herbivoreByCell[neighbour]
                : candidate.Biomass - (candidate.Stress.Total * 2);
            if (score > bestScore)
            {
                (best, bestScore) = (neighbour, score);
            }
        }

        return best;
    }

    private static Dictionary<string, long> TotalsBySpecies(IEnumerable<SpeciesPopulation> populations)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var population in populations)
        {
            totals[population.Species.Value] = totals.GetValueOrDefault(population.Species.Value) + population.Population;
        }

        return totals;
    }

    private static void EmitMilestones(
        Dictionary<string, long> before,
        Dictionary<string, long> after,
        List<EcologySignal> signals)
    {
        foreach (var (species, total) in after.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var previous = before.GetValueOrDefault(species);
            var previousBand = previous / PopulationMilestoneStep;
            var currentBand = total / PopulationMilestoneStep;
            if (currentBand > previousBand)
            {
                signals.Add(new EcologySignal(
                    WorldEventType.PopulationMilestone,
                    0,
                    new SpeciesId(species),
                    (int)Math.Min(int.MaxValue, currentBand * PopulationMilestoneStep)));
            }
        }
    }
}
