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
        var action = SelectAction(charter, cell);
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
        };
    }

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
        var ordered = charter.Goals
            .Select((goal, i) => (Goal: goal, Weight: charter.GoalWeights[i]))
            .OrderByDescending(g => g.Weight)
            .ThenBy(g => (int)g.Goal);

        foreach (var (goal, _) in ordered)
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
