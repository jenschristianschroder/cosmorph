using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Wardens;

/// <summary>The fixed, versioned action grammar available to a Warden.</summary>
public enum WardenActionKind
{
    ProtectHabitat = 0,
    EncourageMigration = 1,
    ConserveEnergy = 2,
    SeekSymbiosis = 3,
    Hunt = 4,
    AdaptTrait = 5,

    /// <summary>Spends the target cell's materials on a construction. Added in warden-actions/2.</summary>
    Build = 6,
}

/// <summary>A bounded, structured proposal. The engine validates it and may reject it.</summary>
public sealed record WardenProposal
{
    public const string GrammarVersion = "warden-actions/2";

    public required WardenId WardenId { get; init; }

    public required WardenActionKind Action { get; init; }

    /// <summary>Cell the action targets. Must be inside the Warden's granted scope.</summary>
    public required int TargetCellIndex { get; init; }

    public required SpeciesId TargetSpecies { get; init; }

    /// <summary>World version the proposal was built against; a stale value is rejected.</summary>
    public required long ExpectedWorldVersion { get; init; }

    public required int BudgetCost { get; init; }

    public required int Impact { get; init; }

    /// <summary>Stable identifier used to make repeated submissions idempotent.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>What to raise. Only meaningful when <see cref="Action"/> is <see cref="WardenActionKind.Build"/>.</summary>
    public ConstructionKind Construction { get; init; }
}

/// <summary>Why a proposal was rejected. Rejections never mutate world state.</summary>
public enum ProposalRejectionReason
{
    None = 0,
    UnknownWarden = 1,
    StaleWorldVersion = 2,
    OutOfScope = 3,
    TabooViolation = 4,
    InsufficientBudget = 5,
    ImpactCeilingReached = 6,
    UnknownSpecies = 7,
    Duplicate = 8,
    OutOfRange = 9,
    InsufficientResources = 10,
}
