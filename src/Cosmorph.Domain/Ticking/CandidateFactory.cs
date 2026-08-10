using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ticking;

/// <summary>
/// Creates the complete set of outcomes the engine is willing to apply for a significant situation.
/// The Worldmind may only pick one of these identifiers; it can never invent an operation.
/// </summary>
public static class CandidateFactory
{
    /// <summary>Minimum number of ticks between two Worldmind decisions for one world.</summary>
    public const int SignificantSituationCooldownTicks = 20;

    public const int MinimumSignalMagnitude = 500;

    public static SituationKind ToSituation(WorldEventType type) => type switch
    {
        WorldEventType.Drought => SituationKind.SevereDrought,
        WorldEventType.Fire => SituationKind.Wildfire,
        WorldEventType.Disease => SituationKind.Plague,
        WorldEventType.Flood => SituationKind.Flood,
        WorldEventType.FoodShortage => SituationKind.Famine,
        _ => SituationKind.None,
    };

    public static DecisionRequest? TryCreate(WorldState state, IReadOnlyList<EcologySignal> signals, long tick)
    {
        if (tick - state.LastSignificantTick < SignificantSituationCooldownTicks)
        {
            return null;
        }

        EcologySignal? chosen = null;
        foreach (var signal in signals)
        {
            if (ToSituation(signal.Type) == SituationKind.None || signal.Magnitude < MinimumSignalMagnitude)
            {
                continue;
            }

            if (chosen is null
                || signal.Magnitude > chosen.Value.Magnitude
                || (signal.Magnitude == chosen.Value.Magnitude && signal.CellIndex < chosen.Value.CellIndex))
            {
                chosen = signal;
            }
        }

        if (chosen is null)
        {
            return null;
        }

        var signalValue = chosen.Value;
        var situation = ToSituation(signalValue.Type);
        var candidates = BuildCandidates(situation, signalValue.CellIndex);

        return new DecisionRequest
        {
            WorldId = state.Id,
            WorldVersion = state.Version,
            Tick = tick,
            Chapter = state.Chapter,
            Situation = situation,
            CellIndex = signalValue.CellIndex,
            Facts = Summarize(state, signalValue.CellIndex),
            Candidates = candidates,
            Fingerprint = Fingerprint(state, tick, situation, signalValue.CellIndex),
        };
    }

    public static string Fingerprint(WorldState state, long tick, SituationKind situation, int cellIndex) =>
        $"{state.Id.Value}:{state.SimulationVersion}:{DecisionRequest.PromptTemplateVersion}:{tick}:{(int)situation}:{cellIndex}";

    public static WorldSummaryFacts Summarize(WorldState state, int cellIndex)
    {
        long temperature = 0;
        long moisture = 0;
        long biomass = 0;
        foreach (var cell in state.Cells)
        {
            temperature += cell.TemperatureDeciC;
            moisture += cell.Moisture.Value;
            biomass += cell.Biomass;
        }

        var count = Math.Max(1, state.Cells.Length);
        var content = state.Content;
        var producers = 0L;
        var herbivores = 0L;
        var predators = 0L;
        foreach (var population in state.Populations)
        {
            switch (content[population.Species].Archetype)
            {
                case SpeciesArchetype.Producer:
                    producers += population.Population;
                    break;
                case SpeciesArchetype.Herbivore:
                    herbivores += population.Population;
                    break;
                default:
                    predators += population.Population;
                    break;
            }
        }

        var cellState = state.Cells[Math.Clamp(cellIndex, 0, state.Cells.Length - 1)];
        return new WorldSummaryFacts
        {
            Day = state.Instant.Day,
            AverageTemperatureDeciC = (int)(temperature / count),
            AverageMoisturePermille = (int)(moisture / count),
            TotalBiomassThousands = (int)Math.Min(int.MaxValue, biomass / 1000),
            ProducerPopulation = (int)Math.Min(int.MaxValue, producers),
            HerbivorePopulation = (int)Math.Min(int.MaxValue, herbivores),
            PredatorPopulation = (int)Math.Min(int.MaxValue, predators),
            CellTemperatureDeciC = cellState.TemperatureDeciC,
            CellMoisturePermille = cellState.Moisture.Value,
            CellBiomass = cellState.Biomass,
        };
    }

    private static IReadOnlyList<CandidateOutcome> BuildCandidates(SituationKind situation, int cellIndex)
    {
        var stress = situation switch
        {
            SituationKind.SevereDrought => StressKindTarget.Drought,
            SituationKind.Wildfire => StressKindTarget.Fire,
            SituationKind.Plague => StressKindTarget.Disease,
            SituationKind.Flood => StressKindTarget.Flood,
            _ => StressKindTarget.Drought,
        };

        return
        [
            new CandidateOutcome
            {
                Id = "contained",
                Label = "The pressure is contained and the region stabilises.",
                Effects =
                [
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustStressPermille, CellIndex = cellIndex, StressTarget = stress, Magnitude = -220 },
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustBiomassPermille, CellIndex = cellIndex, Magnitude = 40 },
                ],
            },
            new CandidateOutcome
            {
                Id = "spreads",
                Label = "The pressure spreads and life retreats.",
                Effects =
                [
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustStressPermille, CellIndex = cellIndex, StressTarget = stress, Magnitude = 160 },
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustBiomassPermille, CellIndex = cellIndex, Magnitude = -180 },
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustPopulationPermille, CellIndex = cellIndex, Magnitude = -120 },
                ],
            },
            new CandidateOutcome
            {
                Id = "adapts",
                Label = "Survivors adapt and pass the trait on.",
                Effects =
                [
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustDroughtTolerance, CellIndex = cellIndex, Magnitude = 60 },
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustColdTolerance, CellIndex = cellIndex, Magnitude = 20 },
                    new OutcomeEffect { Kind = OutcomeEffectKind.AdjustPopulationPermille, CellIndex = cellIndex, Magnitude = -40 },
                ],
            },
        ];
    }
}
