using Cosmorph.Domain.Events;

namespace Cosmorph.Application.Abstractions;

/// <summary>Outcome of asking the Worldmind to choose among engine-created candidates.</summary>
public sealed record GameMasterResponse
{
    public required GameMasterDecision? Decision { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Short, non-sensitive failure category used for audit and metrics.</summary>
    public string? FailureCategory { get; init; }

    public string ModelDeployment { get; init; } = "none";

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }
}

/// <summary>
/// The Worldmind. It receives a canonical state summary plus engine-created candidate outcomes and
/// may only select or rank candidate identifiers and supply narration.
/// </summary>
public interface IGameMaster
{
    /// <summary>Stable identifier of this implementation, recorded in the audit trail.</summary>
    string Name { get; }

    Task<GameMasterResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken);
}
