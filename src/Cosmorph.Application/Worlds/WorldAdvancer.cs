using System.Collections.Immutable;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worldmind;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worlds;

/// <summary>Why an advancement attempt ended the way it did.</summary>
public enum AdvanceStatus
{
    Advanced = 0,
    NotDue = 1,
    Paused = 2,
    LeaseUnavailable = 3,
    NotFound = 4,
    ConcurrencyConflict = 5,
}

/// <summary>Externally observable outcome of advancing one world.</summary>
public sealed record AdvanceResult
{
    public required AdvanceStatus Status { get; init; }

    public long DirectTicks { get; init; }

    public long CompressedTicks { get; init; }

    public int EventsWritten { get; init; }

    public int ModelCalls { get; init; }

    public int FallbackDecisions { get; init; }
}

/// <summary>
/// Advances a single world from its persisted position. This is the only normal writer of simulation
/// outcomes and is invoked by the scheduled TickJob.
/// </summary>
public sealed class WorldAdvancer(
    IWorldStore store,
    IWorldSchedule schedule,
    IGameMaster gameMaster,
    IClock clock,
    SimulationOptions options)
{
    private readonly IWorldStore _store = store;
    private readonly IWorldSchedule _schedule = schedule;
    private readonly IGameMaster _gameMaster = gameMaster;
    private readonly IClock _clock = clock;
    private readonly SimulationOptions _options = options;

    public async Task<AdvanceResult> AdvanceAsync(WorldId worldId, CancellationToken cancellationToken)
    {
        _options.Validate();

        await using var lease = await _store.TryAcquireLeaseAsync(worldId, _options.LeaseDuration, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            return new AdvanceResult { Status = AdvanceStatus.LeaseUnavailable };
        }

        // Always reload inside the lease: another worker may have advanced this world already.
        var current = await _store.TryGetManifestAsync(worldId, cancellationToken).ConfigureAwait(false);
        if (current is not { } held)
        {
            return new AdvanceResult { Status = AdvanceStatus.NotFound };
        }

        var manifest = held.Manifest;
        var state = await _store.TryLoadSnapshotAsync(worldId, manifest.Tick, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The manifest references a snapshot that does not exist.");

        var commands = await _store.ReadPendingCommandsAsync(worldId, 32, cancellationToken).ConfigureAwait(false);
        var appliedCommandIds = new List<string>(commands.Count);
        foreach (var command in commands.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.CommandId, StringComparer.Ordinal))
        {
            if (command.WorldId != worldId)
            {
                // Cross-world commands are impossible through the store, but never trust the payload.
                continue;
            }

            state = WorldCommandApplier.Apply(state, command);
            appliedCommandIds.Add(command.CommandId);
        }

        if (state.IsPaused)
        {
            await CommitAsync(state, new List<WorldEvent>(), manifest, held.ConcurrencyToken, manifest.LastAdvancedAtUtc, appliedCommandIds, cancellationToken)
                .ConfigureAwait(false);
            return new AdvanceResult { Status = AdvanceStatus.Paused };
        }

        var cadence = _options.RealTimePerTick;
        var elapsed = _clock.UtcNow - manifest.LastAdvancedAtUtc;
        var ticksDue = elapsed <= TimeSpan.Zero ? 0 : (long)(elapsed.Ticks / cadence.Ticks);
        if (ticksDue <= 0 && appliedCommandIds.Count == 0)
        {
            return new AdvanceResult { Status = AdvanceStatus.NotDue };
        }

        var events = new List<WorldEvent>();
        long compressed = 0;
        long direct = 0;
        var modelCalls = 0;
        var fallbacks = 0;

        var remaining = ticksDue;
        while (remaining > _options.CoarseThresholdTicks && compressed < _options.MaxCompressedTicksPerRun)
        {
            var chunk = (long)Math.Min(
                Math.Min(_options.CompressChunkTicks, TickEngine.MaxCompressedTicks),
                remaining - _options.CoarseThresholdTicks);
            if (chunk <= 0)
            {
                break;
            }

            var (compressedState, compressedEvents) = TickEngine.Compress(state, chunk);
            state = compressedState;
            events.AddRange(compressedEvents);
            compressed += chunk;
            remaining -= chunk;
        }

        var directBudget = (long)Math.Min(_options.MaxDirectTicksPerRun, remaining);
        for (var i = 0; i < directBudget; i++)
        {
            var outcome = TickEngine.Advance(state);
            state = outcome.State;
            events.AddRange(outcome.Events);
            direct++;

            if (outcome.PendingDecision is { } request)
            {
                var (decidedState, decisionEvents, usedModel, usedFallback) =
                    await ResolveDecisionAsync(state, request, modelCalls, cancellationToken).ConfigureAwait(false);
                state = decidedState;
                events.AddRange(decisionEvents);
                if (usedModel)
                {
                    modelCalls++;
                }

                if (usedFallback)
                {
                    fallbacks++;
                }
            }
        }

        var applied = compressed + direct;
        var advancedTo = manifest.LastAdvancedAtUtc + TimeSpan.FromTicks(cadence.Ticks * applied);
        var committed = await CommitAsync(state, events, manifest, held.ConcurrencyToken, advancedTo, appliedCommandIds, cancellationToken)
            .ConfigureAwait(false);
        if (!committed)
        {
            return new AdvanceResult { Status = AdvanceStatus.ConcurrencyConflict };
        }

        if (remaining - direct > 0)
        {
            // Durable work remains; leave a marker so the next execution continues the catch-up.
            await _schedule.MarkDueAsync(worldId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        return new AdvanceResult
        {
            Status = applied > 0 ? AdvanceStatus.Advanced : AdvanceStatus.NotDue,
            DirectTicks = direct,
            CompressedTicks = compressed,
            EventsWritten = events.Count,
            ModelCalls = modelCalls,
            FallbackDecisions = fallbacks,
        };
    }

    private async Task<(WorldState State, ImmutableArray<WorldEvent> Events, bool UsedModel, bool UsedFallback)> ResolveDecisionAsync(
        WorldState state,
        DecisionRequest request,
        int modelCallsSoFar,
        CancellationToken cancellationToken)
    {
        // A previously accepted decision for the same fingerprint is replayed, never asked again.
        var existing = await _store.TryGetDecisionAsync(state.Id, request.Fingerprint, cancellationToken).ConfigureAwait(false);
        if (existing?.Decision is { } replayed)
        {
            var (replayState, replayEvents, replayRejection) = TickEngine.ApplyDecision(state, request, replayed);
            if (replayRejection == DecisionRejectionReason.None)
            {
                return (replayState, [.. replayEvents], false, replayed.IsFallback);
            }
        }

        GameMasterResponse? response = null;
        var usedModel = false;
        if (modelCallsSoFar < _options.MaxModelCallsPerRun)
        {
            usedModel = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ModelTimeout);
            try
            {
                response = await _gameMaster.DecideAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response = new GameMasterResponse { Decision = null, Succeeded = false, FailureCategory = "timeout" };
            }
        }

        var decision = response is { Succeeded: true, Decision: { } candidate } ? candidate : null;
        var result = DecisionValidationResult.Accepted;

        if (decision is null)
        {
            result = response?.FailureCategory switch
            {
                "timeout" => DecisionValidationResult.Timeout,
                null => DecisionValidationResult.Replayed,
                _ => DecisionValidationResult.TransportError,
            };
        }
        else
        {
            var rejection = TickEngine.Validate(state, request, decision);
            if (rejection != DecisionRejectionReason.None)
            {
                result = rejection == DecisionRejectionReason.UnknownCandidate
                    ? DecisionValidationResult.RejectedUnknownCandidate
                    : DecisionValidationResult.RejectedDomainInvariant;
                decision = null;
            }
        }

        decision ??= DecisionSafety.Fallback(request);

        var (nextState, decisionEvents, applyRejection) = TickEngine.ApplyDecision(state, request, decision);
        if (applyRejection != DecisionRejectionReason.None)
        {
            // The engine's own fallback must always be applicable; refuse to mutate if it is not.
            return (state, [], usedModel, true);
        }

        await _store.WriteDecisionAuditAsync(
            new DecisionAuditRecord
            {
                DecisionId = $"{request.Tick:D20}-{request.Fingerprint.GetHashCode(StringComparison.Ordinal):x8}",
                WorldId = state.Id,
                Tick = request.Tick,
                WorldVersion = request.WorldVersion,
                Situation = request.Situation,
                Fingerprint = request.Fingerprint,
                PromptTemplateVersion = DecisionRequest.PromptTemplateVersion,
                ModelDeployment = response?.ModelDeployment ?? "none",
                Result = result,
                CreatedAtUtc = _clock.UtcNow,
                Decision = decision,
                PromptTokens = response?.PromptTokens ?? 0,
                CompletionTokens = response?.CompletionTokens ?? 0,
            },
            cancellationToken).ConfigureAwait(false);

        return (nextState, [.. decisionEvents], usedModel, decision.IsFallback);
    }

    private async Task<bool> CommitAsync(
        WorldState state,
        List<WorldEvent> events,
        WorldManifest manifest,
        string concurrencyToken,
        DateTimeOffset advancedTo,
        List<string> appliedCommandIds,
        CancellationToken cancellationToken)
    {
        // Order matters: immutable data first, then the manifest. A crash in between is safe because
        // the snapshot and event sequence are content-addressed by tick and sequence number.
        await _store.WriteSnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        if (events.Count > 0)
        {
            await _store.AppendEventsAsync(state.Id, events, cancellationToken).ConfigureAwait(false);
        }

        var next = manifest with
        {
            Tick = state.Tick.Value,
            Version = state.Version,
            Chapter = state.Chapter.Value,
            LastEventSequence = state.LastEventSequence,
            LastAdvancedAtUtc = advancedTo,
            IsPaused = state.IsPaused,
        };

        var replaced = await _store.TryReplaceManifestAsync(next, concurrencyToken, cancellationToken).ConfigureAwait(false);
        if (replaced && appliedCommandIds.Count > 0)
        {
            await _store.MarkCommandsAppliedAsync(state.Id, appliedCommandIds, cancellationToken).ConfigureAwait(false);
        }

        return replaced;
    }
}
