using System.Collections.Immutable;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ticking;

/// <summary>Result of a pure tick transition.</summary>
public sealed record TickOutcome
{
    public required WorldState State { get; init; }

    public required ImmutableArray<WorldEvent> Events { get; init; }

    /// <summary>
    /// Set when the tick produced a significant situation. The application resolves it through the
    /// Worldmind and then calls <see cref="TickEngine.ApplyDecision"/>.
    /// </summary>
    public DecisionRequest? PendingDecision { get; init; }
}
