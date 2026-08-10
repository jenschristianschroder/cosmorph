using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worldmind;

/// <summary>Why a Worldmind response was not used.</summary>
public enum DecisionValidationResult
{
    Accepted = 0,
    RejectedInvalidSchema = 1,
    RejectedUnknownCandidate = 2,
    RejectedDomainInvariant = 3,
    Timeout = 4,
    TransportError = 5,
    Replayed = 6,
}

/// <summary>
/// Audit record of one Worldmind interaction. It stores the accepted decision, the prompt template
/// version, the model deployment identifier, the request fingerprint and the validation result.
/// Raw prompts, raw responses and chain-of-thought are never stored.
/// </summary>
public sealed record DecisionAuditRecord
{
    public const string SchemaVersion = "worldmind-audit/1";

    public required string DecisionId { get; init; }

    public required WorldId WorldId { get; init; }

    public required long Tick { get; init; }

    public required long WorldVersion { get; init; }

    public required SituationKind Situation { get; init; }

    public required string Fingerprint { get; init; }

    public required string PromptTemplateVersion { get; init; }

    public required string ModelDeployment { get; init; }

    public required DecisionValidationResult Result { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public GameMasterDecision? Decision { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }
}
