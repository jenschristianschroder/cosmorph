using System.Collections.Immutable;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ticking;

/// <summary>Why a Worldmind decision was rejected. A rejected decision never mutates world state.</summary>
public enum DecisionRejectionReason
{
    None = 0,
    UnknownCandidate = 1,
    WrongWorld = 2,
    StaleWorldVersion = 3,
    NarrationTooLong = 4,
    InvalidRanking = 5,
    MissingNarration = 6,
}

/// <summary>
/// The deterministic tick pipeline. A tick is a pure transition from an input state plus explicit
/// context to a new state and ordered events.
/// </summary>
public static class TickEngine
{
    /// <summary>Ceiling on ecology signals promoted to Chronicle events in one tick.</summary>
    public const int MaxEventsPerTick = 12;

    /// <summary>Absolute ceiling on Chronicle events written by one tick, including Warden and chapter events.</summary>
    public static int MaxTotalEventsPerTick(WorldState state) =>
        MaxEventsPerTick + (state?.Wardens.Length ?? 0) + 1;

    /// <summary>Coarse catch-up compresses at most this many ticks into one aggregate transition.</summary>
    public const int MaxCompressedTicks = 2_000;

    public static TickOutcome Advance(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsPaused)
        {
            throw new InvalidOperationException("A paused world cannot be advanced.");
        }

        var tick = state.Tick.Next();
        var (cells, populations, constructions, signals) = EcologyStep.Advance(state, tick.Value);

        var advanced = state with
        {
            Tick = tick,
            Cells = cells,
            Populations = populations,
            Constructions = constructions,
            Version = checked(state.Version + 1),
        };

        var proposals = WardenPlanner.Propose(advanced);
        var (afterProposals, resolutions) = ProposalResolver.Resolve(advanced, proposals);

        var events = new List<WorldEvent>();
        var sequence = afterProposals.LastEventSequence;
        var result = afterProposals;

        foreach (var signal in RankSignals(signals))
        {
            events.Add(CreateEvent(result, ++sequence, tick.Value, signal.Type, signal.CellIndex, signal.Species, signal.Magnitude, null));
        }

        foreach (var resolution in resolutions)
        {
            events.Add(CreateEvent(
                result,
                ++sequence,
                tick.Value,
                EventTypeFor(resolution),
                resolution.Proposal.TargetCellIndex,
                resolution.Proposal.TargetSpecies,
                resolution.Accepted ? resolution.AppliedMagnitude : (int)resolution.Rejection,
                null));
        }

        var pending = CandidateFactory.TryCreate(result, signals, tick.Value);
        if (pending is not null)
        {
            result = result with { LastSignificantTick = tick.Value };
        }

        (result, sequence, var chapterEvents) = CloseChapterIfDue(result, tick.Value, sequence);
        events.AddRange(chapterEvents);

        result = result with { LastEventSequence = sequence };

        return new TickOutcome
        {
            State = result,
            Events = [.. events],
            PendingDecision = pending,
        };
    }

    /// <summary>
    /// Applies a Worldmind decision. Validation happens before any mutation, so an invalid decision
    /// produces no partial change and the caller applies <see cref="SafeFallback"/> instead.
    /// </summary>
    public static (WorldState State, IReadOnlyList<WorldEvent> Events, DecisionRejectionReason Rejection) ApplyDecision(
        WorldState state,
        DecisionRequest request,
        GameMasterDecision decision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var rejection = Validate(state, request, decision);
        if (rejection != DecisionRejectionReason.None)
        {
            return (state, [], rejection);
        }

        var candidate = request.Candidates.First(c => string.Equals(c.Id, decision.SelectedCandidateId, StringComparison.Ordinal));
        var applied = ApplyEffects(state, candidate);
        var sequence = applied.LastEventSequence + 1;
        var worldEvent = CreateEvent(
            applied,
            sequence,
            request.Tick,
            decision.IsFallback ? WorldEventType.WorldmindFallback : WorldEventType.WorldmindDecision,
            request.CellIndex,
            null,
            (int)request.Situation,
            decision.Narration);

        return (applied with { LastEventSequence = sequence, Version = checked(applied.Version + 1) }, [worldEvent], DecisionRejectionReason.None);
    }

    /// <summary>Conservative deterministic decision used when the Worldmind fails or answers unsafely.</summary>
    public static GameMasterDecision SafeFallback(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var candidate = request.Candidates
            .OrderBy(c => TotalAbsoluteMagnitude(c))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .First();

        return new GameMasterDecision
        {
            SelectedCandidateId = candidate.Id,
            Ranking = [],
            Narration = candidate.Label,
            Rationale = "Deterministic fallback: the smallest engine-approved change was applied.",
            IsFallback = true,
        };
    }

    public static DecisionRejectionReason Validate(WorldState state, DecisionRequest request, GameMasterDecision decision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        if (request.WorldId != state.Id)
        {
            return DecisionRejectionReason.WrongWorld;
        }

        if (request.WorldVersion != state.Version)
        {
            return DecisionRejectionReason.StaleWorldVersion;
        }

        if (request.Candidates.All(c => !string.Equals(c.Id, decision.SelectedCandidateId, StringComparison.Ordinal)))
        {
            return DecisionRejectionReason.UnknownCandidate;
        }

        if (decision.Ranking.Count > request.Candidates.Count
            || decision.Ranking.Any(id => request.Candidates.All(c => !string.Equals(c.Id, id, StringComparison.Ordinal))))
        {
            return DecisionRejectionReason.InvalidRanking;
        }

        if (string.IsNullOrWhiteSpace(decision.Narration))
        {
            return DecisionRejectionReason.MissingNarration;
        }

        if (decision.Narration.Length > GameMasterDecision.MaxNarrationLength
            || decision.Rationale.Length > GameMasterDecision.MaxRationaleLength)
        {
            return DecisionRejectionReason.NarrationTooLong;
        }

        if (request.Candidates.Any(c => c.Effects.Any(e =>
            e.CellIndex < 0
            || e.CellIndex >= state.Cells.Length
            || Math.Abs(e.Magnitude) > OutcomeEffect.MaxMagnitude)))
        {
            return DecisionRejectionReason.UnknownCandidate;
        }

        return DecisionRejectionReason.None;
    }

    /// <summary>
    /// Coarse catch-up used for long outages. It preserves clamps and conservation limits and always
    /// records an explicit TimeCompressed event.
    /// </summary>
    public static (WorldState State, IReadOnlyList<WorldEvent> Events) Compress(WorldState state, long ticks)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticks, MaxCompressedTicks);
        if (state.IsPaused)
        {
            throw new InvalidOperationException("A paused world cannot be advanced.");
        }

        var tick = new TickNumber(checked(state.Tick.Value + ticks));
        var day = WorldInstant.FromTick(tick, state.TicksPerDay).Day;
        var topology = state.Topology;
        var cells = state.Cells.ToArray();

        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            var temperature = Climate.TemperatureDeciC(state.Seed, topology, cell, day, state.DaysPerYear, state.AxialTiltDegrees);
            var moisture = Climate.MoisturePermille(state.Seed, topology, cell, day, state.DaysPerYear);
            var capacity = WorldGenerator.CarryingCapacity(cell.Biome, temperature, moisture);

            // Long gaps relax toward the local equilibrium instead of replaying every tick.
            var target = capacity * 7 / 10;
            var biomass = cell.Biomass + ((target - cell.Biomass) / 2);

            cells[index] = cell with
            {
                TemperatureDeciC = temperature,
                Moisture = moisture,
                CarryingCapacity = capacity,
                Stress = CellStress.None,
                Biomass = Math.Clamp(biomass, 0, Math.Min(PlanetCell.MaxBiomass, Math.Max(capacity, 1))),
            };
        }

        var populations = state.Populations
            .Select(p =>
            {
                var cell = cells[p.CellIndex];
                var ceiling = Math.Max(10, cell.CarryingCapacity / 4);
                var target = Math.Min(ceiling, Math.Max(10, p.Population));
                return p.WithPopulation(p.Population + ((target - p.Population) / 2)) with
                {
                    Health = Permille.Clamp((p.Health.Value + 800) / 2),
                };
            })
            .ToImmutableArray();

        var sequence = state.LastEventSequence + 1;
        var result = state with
        {
            Tick = tick,
            Cells = [.. cells],
            Populations = WorldGenerator.CanonicalOrder(populations),
            Version = checked(state.Version + 1),
            LastEventSequence = sequence,
        };

        var worldEvent = CreateEvent(result, sequence, tick.Value, WorldEventType.TimeCompressed, null, null, (int)Math.Min(int.MaxValue, ticks), null);
        return (result, [worldEvent]);
    }

    /// <summary>
    /// Raising a structure gets its own Chronicle type so spectators can tell it apart from the other
    /// things a Warden does. A rejected proposal is always reported as a rejection.
    /// </summary>
    private static WorldEventType EventTypeFor(ProposalResolution resolution)
    {
        if (!resolution.Accepted)
        {
            return WorldEventType.WardenProposalRejected;
        }

        return resolution.Proposal.Action == WardenActionKind.Build
            ? WorldEventType.ConstructionRaised
            : WorldEventType.WardenAction;
    }

    private static IEnumerable<EcologySignal> RankSignals(IReadOnlyList<EcologySignal> signals) =>
        signals
            .OrderByDescending(s => s.Magnitude)
            .ThenBy(s => (int)s.Type)
            .ThenBy(s => s.CellIndex)
            .Take(MaxEventsPerTick);

    private static (WorldState State, long Sequence, IReadOnlyList<WorldEvent> Events) CloseChapterIfDue(
        WorldState state,
        long tick,
        long sequence)
    {
        if (tick - state.ChapterStartTick < state.TicksPerChapter)
        {
            return (state, sequence, []);
        }

        var renewed = state.Wardens.Select(w => w.RenewForChapter()).ToImmutableArray();
        var closed = state with
        {
            Chapter = state.Chapter.Next(),
            ChapterStartTick = tick,
            Wardens = renewed,
        };

        var worldEvent = CreateEvent(state, ++sequence, tick, WorldEventType.ChapterClosed, null, null, state.Chapter.Value, null);
        return (closed, sequence, [worldEvent]);
    }

    private static WorldState ApplyEffects(WorldState state, CandidateOutcome candidate)
    {
        var cells = state.Cells.ToArray();
        var populations = state.Populations.ToArray();

        foreach (var effect in candidate.Effects)
        {
            var magnitude = Math.Clamp(effect.Magnitude, -OutcomeEffect.MaxMagnitude, OutcomeEffect.MaxMagnitude);
            var cell = cells[effect.CellIndex];
            switch (effect.Kind)
            {
                case OutcomeEffectKind.AdjustBiomassPermille:
                    cells[effect.CellIndex] = cell.WithBiomass(cell.Biomass + (cell.Biomass * magnitude / 1000));
                    break;

                case OutcomeEffectKind.AdjustStressPermille:
                    cells[effect.CellIndex] = cell with { Stress = AdjustStress(cell.Stress, effect.StressTarget, magnitude) };
                    break;

                case OutcomeEffectKind.AdjustPopulationPermille:
                    Adjust(populations, effect.CellIndex, effect.Species, p => p.WithPopulation(p.Population + (p.Population * magnitude / 1000)));
                    break;

                case OutcomeEffectKind.AdjustDroughtTolerance:
                    Adjust(populations, effect.CellIndex, effect.Species, p => p with { Traits = p.Traits.Adjust(0, magnitude) });
                    break;

                case OutcomeEffectKind.AdjustColdTolerance:
                    Adjust(populations, effect.CellIndex, effect.Species, p => p with { Traits = p.Traits.Adjust(magnitude, 0) });
                    break;

                default:
                    break;
            }
        }

        return state with
        {
            Cells = [.. cells],
            Populations = WorldGenerator.CanonicalOrder([.. populations]),
        };
    }

    private static void Adjust(
        SpeciesPopulation[] populations,
        int cellIndex,
        SpeciesId? species,
        Func<SpeciesPopulation, SpeciesPopulation> change)
    {
        for (var i = 0; i < populations.Length; i++)
        {
            if (populations[i].CellIndex != cellIndex)
            {
                continue;
            }

            if (species is not null && populations[i].Species != species.Value)
            {
                continue;
            }

            populations[i] = change(populations[i]);
        }
    }

    private static CellStress AdjustStress(CellStress stress, StressKindTarget target, int magnitude) => target switch
    {
        StressKindTarget.Drought => stress with { Drought = stress.Drought.Add(magnitude) },
        StressKindTarget.Disease => stress with { Disease = stress.Disease.Add(magnitude) },
        StressKindTarget.Fire => stress with { Fire = stress.Fire.Add(magnitude) },
        StressKindTarget.Flood => stress with { Flood = stress.Flood.Add(magnitude) },
        _ => stress,
    };

    private static int TotalAbsoluteMagnitude(CandidateOutcome candidate) =>
        candidate.Effects.Sum(e => Math.Abs(e.Magnitude));

    private static WorldEvent CreateEvent(
        WorldState state,
        long sequence,
        long tick,
        WorldEventType type,
        int? cellIndex,
        SpeciesId? species,
        int magnitude,
        string? narration) => new()
        {
            Sequence = sequence,
            Tick = tick,
            Type = type,
            Chapter = state.Chapter,
            CellIndex = cellIndex,
            Species = species,
            Magnitude = magnitude,
            Narration = narration,
            SimulationVersion = state.SimulationVersion,
            ContentVersion = state.ContentVersion,
        };
}
