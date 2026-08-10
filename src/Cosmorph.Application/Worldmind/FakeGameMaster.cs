using Cosmorph.Application.Abstractions;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Random;
using Cosmorph.Domain.Ticking;

namespace Cosmorph.Application.Worldmind;

/// <summary>
/// Deterministic stand-in for the Worldmind. It is used by tests and by local development so the
/// vertical slice runs with no Azure resources and no model. Production startup rejects it.
/// </summary>
public sealed class FakeGameMaster : IGameMaster
{
    public const string DeploymentName = "fake-worldmind";

    public string Name => "FakeGameMaster";

    public Task<GameMasterResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var ordered = request.Candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var pick = DeterministicRandom.Next(
            HashFingerprint(request.Fingerprint),
            ordered.Count,
            request.Tick,
            request.CellIndex,
            (int)request.Situation);

        var selected = ordered[pick];
        var decision = new GameMasterDecision
        {
            SelectedCandidateId = selected.Id,
            Ranking = [.. ordered.Where(c => c.Id != selected.Id).Select(c => c.Id)],
            Narration = Narrate(request.Situation, selected),
            Rationale = "Deterministic local Worldmind selected an engine-approved candidate.",
            IsFallback = false,
        };

        return Task.FromResult(new GameMasterResponse
        {
            Decision = decision,
            Succeeded = true,
            ModelDeployment = DeploymentName,
        });
    }

    private static ulong HashFingerprint(string fingerprint)
    {
        ulong hash = 1469598103934665603;
        foreach (var c in fingerprint)
        {
            hash = unchecked((hash ^ c) * 1099511628211);
        }

        return hash;
    }

    private static string Narrate(SituationKind situation, CandidateOutcome candidate)
    {
        var prefix = situation switch
        {
            SituationKind.SevereDrought => "The rains fail.",
            SituationKind.Wildfire => "Fire crosses the ridge.",
            SituationKind.Plague => "A sickness moves through the herds.",
            SituationKind.Flood => "The water rises.",
            SituationKind.Famine => "The grazing runs out.",
            _ => "The world shifts.",
        };

        var narration = $"{prefix} {candidate.Label}";
        return narration.Length > GameMasterDecision.MaxNarrationLength
            ? narration[..GameMasterDecision.MaxNarrationLength]
            : narration;
    }
}

/// <summary>A Worldmind that always fails, used to prove the deterministic fallback path.</summary>
public sealed class UnavailableGameMaster : IGameMaster
{
    public string Name => "UnavailableGameMaster";

    public Task<GameMasterResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GameMasterResponse
        {
            Decision = null,
            Succeeded = false,
            FailureCategory = "unavailable",
            ModelDeployment = "none",
        });
}

/// <summary>Helpers shared by every Worldmind implementation.</summary>
public static class DecisionSafety
{
    /// <summary>Deterministic safe fallback used whenever a model answer cannot be trusted.</summary>
    public static GameMasterDecision Fallback(DecisionRequest request) => TickEngine.SafeFallback(request);
}
