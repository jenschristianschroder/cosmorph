using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Events;

/// <summary>Significant situations for which the engine asks the Worldmind to choose an outcome.</summary>
public enum SituationKind
{
    None = 0,
    SevereDrought = 1,
    Wildfire = 2,
    Plague = 3,
    Flood = 4,
    Famine = 5,
}

/// <summary>Kinds of effect an engine-created candidate may contain.</summary>
public enum OutcomeEffectKind
{
    AdjustBiomassPermille = 0,
    AdjustStressPermille = 1,
    AdjustPopulationPermille = 2,
    AdjustDroughtTolerance = 3,
    AdjustColdTolerance = 4,
}

/// <summary>A single bounded effect. All targets and magnitudes are created by the engine.</summary>
public sealed record OutcomeEffect
{
    public const int MaxMagnitude = 400;

    public required OutcomeEffectKind Kind { get; init; }

    public required int CellIndex { get; init; }

    public SpeciesId? Species { get; init; }

    public StressKindTarget StressTarget { get; init; }

    /// <summary>Signed magnitude bounded by <see cref="MaxMagnitude"/>.</summary>
    public required int Magnitude { get; init; }
}

/// <summary>Which typed stress indicator an effect targets.</summary>
public enum StressKindTarget
{
    None = 0,
    Drought = 1,
    Disease = 2,
    Fire = 3,
    Flood = 4,
}

/// <summary>
/// An outcome the engine is willing to apply. The Worldmind may only select one of these identifiers.
/// </summary>
public sealed record CandidateOutcome
{
    public required string Id { get; init; }

    /// <summary>Engine-authored short label. Never derived from user or model text.</summary>
    public required string Label { get; init; }

    public required IReadOnlyList<OutcomeEffect> Effects { get; init; }
}

/// <summary>Everything the engine is prepared to disclose about a significant situation.</summary>
public sealed record DecisionRequest
{
    public const string PromptTemplateVersion = "worldmind-decide/1";

    public required WorldId WorldId { get; init; }

    public required long WorldVersion { get; init; }

    public required long Tick { get; init; }

    public required ChapterId Chapter { get; init; }

    public required SituationKind Situation { get; init; }

    public required int CellIndex { get; init; }

    /// <summary>Canonical, size-bounded world summary supplied as data, never as instructions.</summary>
    public required WorldSummaryFacts Facts { get; init; }

    public required IReadOnlyList<CandidateOutcome> Candidates { get; init; }

    /// <summary>Stable fingerprint of the request, used for deduplication and replay.</summary>
    public required string Fingerprint { get; init; }
}

/// <summary>Bounded numeric facts describing the world at the moment of a decision.</summary>
public sealed record WorldSummaryFacts
{
    public required long Day { get; init; }

    public required int AverageTemperatureDeciC { get; init; }

    public required int AverageMoisturePermille { get; init; }

    public required int TotalBiomassThousands { get; init; }

    public required int ProducerPopulation { get; init; }

    public required int HerbivorePopulation { get; init; }

    public required int PredatorPopulation { get; init; }

    public required int CellTemperatureDeciC { get; init; }

    public required int CellMoisturePermille { get; init; }

    public required int CellBiomass { get; init; }
}

/// <summary>A validated Worldmind decision. It can only ever reference engine-created candidates.</summary>
public sealed record GameMasterDecision
{
    public const int MaxNarrationLength = 240;
    public const int MaxRationaleLength = 240;

    public required string SelectedCandidateId { get; init; }

    public required IReadOnlyList<string> Ranking { get; init; }

    /// <summary>Short spectator narration. Stored separately from authoritative values.</summary>
    public required string Narration { get; init; }

    /// <summary>Short audit rationale. Chain-of-thought is never requested or stored.</summary>
    public required string Rationale { get; init; }

    /// <summary>True when the engine produced the decision deterministically after a model failure.</summary>
    public bool IsFallback { get; init; }
}
