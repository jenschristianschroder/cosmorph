using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worlds;

/// <summary>Kinds of validated command a non-tick process may create for a later tick to apply.</summary>
public enum WorldCommandKind
{
    SetWardenCharter = 0,
    PauseWorld = 1,
    ResumeWorld = 2,
}

/// <summary>
/// A validated, world-scoped command. Mutations outside the TickJob never write simulation outcomes
/// directly; they create a command that the next tick applies.
/// </summary>
public sealed record WorldCommand
{
    public required string CommandId { get; init; }

    public required WorldId WorldId { get; init; }

    public required WorldCommandKind Kind { get; init; }

    /// <summary>Server-resolved actor identifier. Never taken from a request body.</summary>
    public required string ActorId { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public WardenCharter? Charter { get; init; }
}
