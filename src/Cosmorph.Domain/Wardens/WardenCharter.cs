using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Wardens;

/// <summary>Bounded enum goals a Warden may pursue.</summary>
public enum WardenGoal
{
    Preserve = 0,
    Expand = 1,
    Adapt = 2,
    Cooperate = 3,
    Hunt = 4,
}

/// <summary>Bounded enum taboos a Warden must never violate.</summary>
public enum WardenTaboo
{
    NeverHunt = 0,
    NeverMigrate = 1,
    NeverBurn = 2,
    NeverCompete = 3,
}

/// <summary>
/// A user-configured but capability-limited charter. Free-form text is descriptive data only and is
/// never interpreted as an instruction.
/// </summary>
public sealed record WardenCharter
{
    public const int MaxGoals = 3;
    public const int MaxTaboos = 4;
    public const int MaxWeight = 100;
    public const int MaxBudget = 100;
    public const int MaxImpactCeiling = 100;
    public const int MaxDisplayNameLength = 40;

    /// <summary>Ceiling on the controlled region, so a Warden governs a place rather than a planet.</summary>
    public const int MaxRegionCells = 512;

    public required WardenId Id { get; init; }

    /// <summary>Untrusted descriptive label. Never used as an instruction and always encoded on output.</summary>
    public required string DisplayName { get; init; }

    public required SpeciesId ControlledSpecies { get; init; }

    /// <summary>Cell indices the Warden may act on. Empty means the whole world is out of scope.</summary>
    public required IReadOnlyList<int> ControlledRegion { get; init; }

    public required IReadOnlyList<WardenGoal> Goals { get; init; }

    /// <summary>Priority weight per goal, aligned by index with <see cref="Goals"/>. Each is 1..100.</summary>
    public required IReadOnlyList<int> GoalWeights { get; init; }

    public required IReadOnlyList<WardenTaboo> Taboos { get; init; }

    /// <summary>Remaining renewable action budget.</summary>
    public required int ActionBudget { get; init; }

    /// <summary>Budget granted back at each chapter boundary.</summary>
    public required int BudgetRenewalPerChapter { get; init; }

    /// <summary>Maximum accumulated impact allowed within one chapter.</summary>
    public required int ImpactCeilingPerChapter { get; init; }

    public required int ImpactUsedThisChapter { get; init; }

    public bool Allows(WardenActionKind action) => action switch
    {
        WardenActionKind.Hunt => !Taboos.Contains(WardenTaboo.NeverHunt),
        WardenActionKind.EncourageMigration => !Taboos.Contains(WardenTaboo.NeverMigrate),
        WardenActionKind.SeekSymbiosis => !Taboos.Contains(WardenTaboo.NeverCompete),
        _ => true,
    };

    public bool CoversCell(int cellIndex) => ControlledRegion.Contains(cellIndex);

    public WardenCharter Spend(int cost, int impact) => this with
    {
        ActionBudget = Math.Max(0, ActionBudget - cost),
        ImpactUsedThisChapter = Math.Min(MaxImpactCeiling, ImpactUsedThisChapter + impact),
    };

    public WardenCharter RenewForChapter() => this with
    {
        ActionBudget = Math.Min(MaxBudget, ActionBudget + BudgetRenewalPerChapter),
        ImpactUsedThisChapter = 0,
    };

    /// <summary>Validates every bound of the charter. Returns null when the charter is acceptable.</summary>
    public string? Validate(int cellCount)
    {
        if (DisplayName.Length is 0 or > MaxDisplayNameLength)
        {
            return "Warden display name length is out of range.";
        }

        if (Goals.Count is 0 or > MaxGoals)
        {
            return "A charter needs one to three goals.";
        }

        if (GoalWeights.Count != Goals.Count)
        {
            return "Each goal needs exactly one weight.";
        }

        if (Goals.Distinct().Count() != Goals.Count)
        {
            return "Goals must be distinct.";
        }

        if (GoalWeights.Any(w => w is < 1 or > MaxWeight))
        {
            return "Goal weights must be within 1..100.";
        }

        if (Taboos.Count > MaxTaboos || Taboos.Distinct().Count() != Taboos.Count)
        {
            return "Taboos must be distinct and bounded.";
        }

        if (ControlledRegion.Count is 0 || ControlledRegion.Count > MaxRegionCells)
        {
            return "The controlled region must contain one to 512 cells.";
        }

        if (ControlledRegion.Any(c => c < 0 || c >= cellCount))
        {
            return "The controlled region references a cell outside this world.";
        }

        if (ActionBudget is < 0 or > MaxBudget
            || BudgetRenewalPerChapter is < 0 or > MaxBudget
            || ImpactCeilingPerChapter is < 0 or > MaxImpactCeiling
            || ImpactUsedThisChapter is < 0 or > MaxImpactCeiling)
        {
            return "Budget or impact values are out of range.";
        }

        return null;
    }
}
