using System.Collections.Immutable;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Wardens;

/// <summary>Outcome of validating and resolving one proposal.</summary>
public readonly record struct ProposalResolution(
    WardenProposal Proposal,
    ProposalRejectionReason Rejection,
    int AppliedMagnitude)
{
    public bool Accepted => Rejection == ProposalRejectionReason.None;
}

/// <summary>
/// Deterministic validation and resolution of Warden proposals. A rejected proposal never mutates state.
/// </summary>
public static class ProposalResolver
{
    public const int MaxAcceptedProposalsPerTick = 8;

    public static (WorldState State, IReadOnlyList<ProposalResolution> Resolutions) Resolve(
        WorldState state,
        IReadOnlyList<WardenProposal> proposals)
    {
        var resolutions = new List<ProposalResolution>(proposals.Count);
        var cells = state.Cells.ToArray();
        var populations = state.Populations.ToArray();
        var wardens = state.Wardens.ToBuilder();
        var constructions = state.Constructions.ToList();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var accepted = 0;

        foreach (var proposal in proposals.OrderBy(p => p.WardenId.Value, StringComparer.Ordinal).ThenBy(p => p.IdempotencyKey, StringComparer.Ordinal))
        {
            var reason = Validate(state, wardens, constructions, cells, proposal, seenKeys, accepted);
            if (reason != ProposalRejectionReason.None)
            {
                resolutions.Add(new ProposalResolution(proposal, reason, 0));
                continue;
            }

            seenKeys.Add(proposal.IdempotencyKey);
            var index = IndexOfWarden(wardens, proposal.WardenId);
            var charter = wardens[index];
            var magnitude = Apply(proposal, cells, populations, constructions);
            wardens[index] = charter.Spend(proposal.BudgetCost, proposal.Impact);
            accepted++;
            resolutions.Add(new ProposalResolution(proposal, ProposalRejectionReason.None, magnitude));
        }

        var newState = state with
        {
            Cells = [.. cells],
            Populations = WorldGenerator.CanonicalOrder([.. populations]),
            Wardens = wardens.ToImmutable(),
            Constructions = [.. constructions.OrderBy(c => c.CellIndex)],
        };

        return (newState, resolutions);
    }

    private static ProposalRejectionReason Validate(
        WorldState state,
        ImmutableArray<WardenCharter>.Builder wardens,
        List<Construction> constructions,
        PlanetCell[] cells,
        WardenProposal proposal,
        HashSet<string> seenKeys,
        int accepted)
    {
        if (accepted >= MaxAcceptedProposalsPerTick)
        {
            return ProposalRejectionReason.ImpactCeilingReached;
        }

        if (seenKeys.Contains(proposal.IdempotencyKey))
        {
            return ProposalRejectionReason.Duplicate;
        }

        var index = IndexOfWarden(wardens, proposal.WardenId);
        if (index < 0)
        {
            return ProposalRejectionReason.UnknownWarden;
        }

        var charter = wardens[index];

        if (proposal.ExpectedWorldVersion != state.Version)
        {
            return ProposalRejectionReason.StaleWorldVersion;
        }

        if (proposal.BudgetCost is < 1 or > 10 || proposal.Impact is < 0 or > 10)
        {
            return ProposalRejectionReason.OutOfRange;
        }

        if (proposal.TargetCellIndex < 0 || proposal.TargetCellIndex >= state.Cells.Length)
        {
            return ProposalRejectionReason.OutOfRange;
        }

        if (!charter.CoversCell(proposal.TargetCellIndex))
        {
            return ProposalRejectionReason.OutOfScope;
        }

        if (proposal.TargetSpecies != charter.ControlledSpecies)
        {
            return ProposalRejectionReason.OutOfScope;
        }

        if (!state.Content.TryGet(proposal.TargetSpecies, out _))
        {
            return ProposalRejectionReason.UnknownSpecies;
        }

        if (!charter.Allows(proposal.Action))
        {
            return ProposalRejectionReason.TabooViolation;
        }

        if (charter.ActionBudget < proposal.BudgetCost)
        {
            return ProposalRejectionReason.InsufficientBudget;
        }

        if (charter.ImpactUsedThisChapter + proposal.Impact > charter.ImpactCeilingPerChapter)
        {
            return ProposalRejectionReason.ImpactCeilingReached;
        }

        if (proposal.Action == WardenActionKind.Build)
        {
            return ValidateBuild(constructions, cells, proposal);
        }

        return ProposalRejectionReason.None;
    }

    private static ProposalRejectionReason ValidateBuild(
        List<Construction> constructions,
        PlanetCell[] cells,
        WardenProposal proposal)
    {
        if (proposal.Construction == ConstructionKind.None)
        {
            return ProposalRejectionReason.OutOfRange;
        }

        var cell = cells[proposal.TargetCellIndex];
        if (!cell.IsLand)
        {
            return ProposalRejectionReason.OutOfRange;
        }

        var existing = FindConstruction(constructions, proposal.TargetCellIndex);
        if (existing >= 0)
        {
            var standing = constructions[existing];

            // One structure per cell, and only its own kind can be built up further.
            if (standing.Kind != proposal.Construction || standing.Level >= Construction.MaxLevel)
            {
                return ProposalRejectionReason.OutOfRange;
            }
        }

        var level = existing >= 0 ? constructions[existing].Level + 1 : 1;
        return cell.Resources.Covers(Construction.CostFor(proposal.Construction, level))
            ? ProposalRejectionReason.None
            : ProposalRejectionReason.InsufficientResources;
    }

    private static int FindConstruction(List<Construction> constructions, int cellIndex)
    {
        for (var i = 0; i < constructions.Count; i++)
        {
            if (constructions[i].CellIndex == cellIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfWarden(ImmutableArray<WardenCharter>.Builder wardens, WardenId id)
    {
        for (var i = 0; i < wardens.Count; i++)
        {
            if (wardens[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Apply(
        WardenProposal proposal,
        PlanetCell[] cells,
        SpeciesPopulation[] populations,
        List<Construction> constructions)
    {
        var cell = cells[proposal.TargetCellIndex];
        switch (proposal.Action)
        {
            case WardenActionKind.ProtectHabitat:
                cells[proposal.TargetCellIndex] = cell with { Stress = cell.Stress.Decay(60) };
                return 60;

            case WardenActionKind.ConserveEnergy:
                return AdjustPopulations(populations, proposal, p => p with
                {
                    Energy = Math.Clamp(p.Energy + 60, 0, 1000),
                    Health = p.Health.Add(20),
                });

            case WardenActionKind.SeekSymbiosis:
                cells[proposal.TargetCellIndex] = cell.WithBiomass(cell.Biomass + (cell.CarryingCapacity / 50));
                return cell.CarryingCapacity / 50;

            case WardenActionKind.AdaptTrait:
                return AdjustPopulations(populations, proposal, p => p with { Traits = p.Traits.Adjust(4, 4) });

            case WardenActionKind.EncourageMigration:
                return AdjustPopulations(populations, proposal, p => p with { Energy = Math.Clamp(p.Energy + 30, 0, 1000) });

            case WardenActionKind.Hunt:
                return AdjustPopulations(populations, proposal, p => p.WithPopulation(p.Population - (p.Population / 50)));

            case WardenActionKind.Build:
                return Build(proposal, cells, constructions);

            default:
                return 0;
        }
    }

    /// <summary>
    /// Raises a new construction or adds a level to the one already standing, paying for it out of the
    /// target cell's own stock. Validation has already established that the stock covers the cost.
    /// </summary>
    private static int Build(WardenProposal proposal, PlanetCell[] cells, List<Construction> constructions)
    {
        var existing = FindConstruction(constructions, proposal.TargetCellIndex);
        var level = existing >= 0 ? constructions[existing].Level + 1 : 1;
        var cell = cells[proposal.TargetCellIndex];

        if (!cell.Resources.TrySpend(Construction.CostFor(proposal.Construction, level), out var remaining))
        {
            return 0;
        }

        cells[proposal.TargetCellIndex] = cell with { Resources = remaining };

        if (existing >= 0)
        {
            constructions[existing] = constructions[existing].Upgrade();
        }
        else
        {
            constructions.Add(Construction.Raise(proposal.TargetCellIndex, proposal.Construction));
        }

        return level;
    }

    private static int AdjustPopulations(
        SpeciesPopulation[] populations,
        WardenProposal proposal,
        Func<SpeciesPopulation, SpeciesPopulation> change)
    {
        var magnitude = 0;
        for (var i = 0; i < populations.Length; i++)
        {
            if (populations[i].CellIndex != proposal.TargetCellIndex || populations[i].Species != proposal.TargetSpecies)
            {
                continue;
            }

            var before = populations[i].Population;
            populations[i] = change(populations[i]);
            magnitude = Math.Max(magnitude, Math.Abs(populations[i].Population - before));
        }

        return magnitude;
    }
}
