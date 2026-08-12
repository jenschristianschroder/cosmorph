using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Wardens;

/// <summary>
/// Generates bounded proposals from a charter and the visible state. A Warden never chooses an action
/// outside the fixed grammar, its granted scope, its taboos or its remaining budget.
/// </summary>
public static class WardenPlanner
{
    public const int MaxProposalsPerWardenPerTick = 1;

    public static IReadOnlyList<WardenProposal> Propose(WorldState state)
    {
        var proposals = new List<WardenProposal>();
        foreach (var charter in state.Wardens.OrderBy(w => w.Id.Value, StringComparer.Ordinal))
        {
            var proposal = ProposeFor(state, charter);
            if (proposal is not null)
            {
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    private static WardenProposal? ProposeFor(WorldState state, WardenCharter charter)
    {
        if (charter.ActionBudget <= 0 || charter.ImpactUsedThisChapter >= charter.ImpactCeilingPerChapter)
        {
            return null;
        }

        if (!state.Content.TryGet(charter.ControlledSpecies, out _))
        {
            return null;
        }

        var target = SelectCell(state, charter);
        if (target is null)
        {
            return null;
        }

        var cell = target.Value;
        var construction = ChooseConstruction(state, charter, cell);
        var action = construction == ConstructionKind.None
            ? SelectAction(charter, cell)
            : WardenActionKind.Build;
        if (action is null)
        {
            return null;
        }

        return new WardenProposal
        {
            WardenId = charter.Id,
            Action = action.Value,
            TargetCellIndex = cell.Index,
            TargetSpecies = charter.ControlledSpecies,
            ExpectedWorldVersion = state.Version,
            BudgetCost = 1,
            Impact = 2,
            IdempotencyKey = $"{state.Id.Value}:{charter.Id.Value}:{state.Tick.Value}",
            Construction = construction,
        };
    }

    /// <summary>
    /// Picks what to build on the target cell, or <see cref="ConstructionKind.None"/> when building is
    /// not the right move. A Warden only proposes a build it can already pay for out of the cell's own
    /// stock, so materials — not the action budget — are what gate construction.
    /// </summary>
    private static ConstructionKind ChooseConstruction(WorldState state, WardenCharter charter, PlanetCell cell)
    {
        if (!cell.IsLand)
        {
            return ConstructionKind.None;
        }

        var standing = state.Constructions.FirstOrDefault(c => c.CellIndex == cell.Index);
        var level = 1;
        ConstructionKind kind;

        if (standing.IsStanding)
        {
            if (standing.Level >= Construction.MaxLevel)
            {
                return ConstructionKind.None;
            }

            // Only the structure already there can be built up further; one per cell.
            kind = standing.Kind;
            level = standing.Level + 1;
        }
        else
        {
            kind = PreferredKind(charter);
            if (kind == ConstructionKind.None)
            {
                return ConstructionKind.None;
            }
        }

        return cell.Resources.Covers(Construction.CostFor(kind, level)) ? kind : ConstructionKind.None;
    }

    /// <summary>Maps the charter's highest-weighted goal to the structure that serves it.</summary>
    private static ConstructionKind PreferredKind(WardenCharter charter)
    {
        foreach (var (goal, _) in OrderedGoals(charter))
        {
            var kind = goal switch
            {
                WardenGoal.Preserve => ConstructionKind.Shelter,
                WardenGoal.Expand => ConstructionKind.Terrace,
                WardenGoal.Adapt => ConstructionKind.Windbreak,
                _ => ConstructionKind.None,
            };

            if (kind != ConstructionKind.None)
            {
                return kind;
            }
        }

        return ConstructionKind.None;
    }

    private static IEnumerable<(WardenGoal Goal, int Weight)> OrderedGoals(WardenCharter charter) =>
        charter.Goals
            .Select((goal, i) => (Goal: goal, Weight: charter.GoalWeights[i]))
            .OrderByDescending(g => g.Weight)
            .ThenBy(g => (int)g.Goal);

    private static PlanetCell? SelectCell(WorldState state, WardenCharter charter)
    {
        PlanetCell? worst = null;
        foreach (var index in charter.ControlledRegion.Order())
        {
            if (index < 0 || index >= state.Cells.Length)
            {
                continue;
            }

            var cell = state.Cells[index];
            if (!cell.IsLand)
            {
                continue;
            }

            if (worst is null || cell.Vitality.Value < worst.Value.Vitality.Value)
            {
                worst = cell;
            }
        }

        return worst;
    }

    private static WardenActionKind? SelectAction(WardenCharter charter, PlanetCell cell)
    {
        foreach (var (goal, _) in OrderedGoals(charter))
        {
            var candidate = goal switch
            {
                WardenGoal.Preserve => cell.Stress.Dominant == StressKind.None
                    ? WardenActionKind.ConserveEnergy
                    : WardenActionKind.ProtectHabitat,
                WardenGoal.Expand => WardenActionKind.EncourageMigration,
                WardenGoal.Adapt => WardenActionKind.AdaptTrait,
                WardenGoal.Cooperate => WardenActionKind.SeekSymbiosis,
                WardenGoal.Hunt => WardenActionKind.Hunt,
                _ => WardenActionKind.ConserveEnergy,
            };

            if (charter.Allows(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
