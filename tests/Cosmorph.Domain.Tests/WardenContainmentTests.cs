using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Tests;

/// <summary>
/// Negative tests for the Warden containment boundary: scope, taboos, budget, staleness and duplicates.
/// </summary>
public sealed class WardenContainmentTests
{
    private static WorldState World()
    {
        var state = WorldGenerator.Create(WorldId.Parse("warden-world"), "Warden World", new WorldSeed(2024), 32, 16);
        return state with { Wardens = [Charter("moss-keeper", "verdant-moss", state), Charter("grazer-keeper", "cliff-grazer", state)] };
    }

    private static WardenCharter Charter(string id, string species, WorldState state) => new()
    {
        Id = WardenId.Parse(id),
        DisplayName = id,
        ControlledSpecies = new SpeciesId(species),
        ControlledRegion = [.. state.Populations.Where(p => p.Species.Value == species).Select(p => p.CellIndex).Distinct().Take(24)],
        Goals = [WardenGoal.Preserve, WardenGoal.Adapt],
        GoalWeights = [70, 30],
        Taboos = [],
        ActionBudget = 20,
        BudgetRenewalPerChapter = 10,
        ImpactCeilingPerChapter = 40,
        ImpactUsedThisChapter = 0,
    };

    private static WardenProposal Proposal(WardenCharter charter, long version, WardenActionKind action = WardenActionKind.ProtectHabitat) =>
        new()
        {
            WardenId = charter.Id,
            Action = action,
            TargetCellIndex = charter.ControlledRegion[0],
            TargetSpecies = charter.ControlledSpecies,
            ExpectedWorldVersion = version,
            BudgetCost = 1,
            Impact = 1,
            IdempotencyKey = "key-1",
        };

    [Fact]
    public void GeneratedWorldsHaveValidCharters()
    {
        var world = World();
        Assert.NotEmpty(world.Wardens);
        Assert.All(world.Wardens, w => Assert.Null(w.Validate(world.Cells.Length)));
    }

    [Fact]
    public void ProposalOutsideTheControlledRegionIsRejected()
    {
        var world = World();
        var charter = world.Wardens[0];
        var outside = Enumerable.Range(0, world.Cells.Length).First(i => !charter.CoversCell(i));
        var proposal = Proposal(charter, world.Version) with { TargetCellIndex = outside };

        var (state, resolutions) = ProposalResolver.Resolve(world, [proposal]);

        Assert.Equal(ProposalRejectionReason.OutOfScope, resolutions[0].Rejection);
        Assert.Equal(world.Version, state.Version);
        Assert.True(world.Cells.SequenceEqual(state.Cells));
        Assert.True(world.Populations.SequenceEqual(state.Populations));
    }

    [Fact]
    public void ProposalForAnotherWardensSpeciesIsRejected()
    {
        var world = World();
        var charter = world.Wardens[0];
        var other = world.Wardens.First(w => w.ControlledSpecies != charter.ControlledSpecies);
        var proposal = Proposal(charter, world.Version) with { TargetSpecies = other.ControlledSpecies };

        var (_, resolutions) = ProposalResolver.Resolve(world, [proposal]);
        Assert.Equal(ProposalRejectionReason.OutOfScope, resolutions[0].Rejection);
    }

    [Fact]
    public void StaleWorldVersionIsRejected()
    {
        var world = World();
        var proposal = Proposal(world.Wardens[0], world.Version - 1);

        var (_, resolutions) = ProposalResolver.Resolve(world, [proposal]);
        Assert.Equal(ProposalRejectionReason.StaleWorldVersion, resolutions[0].Rejection);
    }

    [Fact]
    public void TabooedActionIsRejected()
    {
        var world = World();
        var charter = world.Wardens[0] with { Taboos = [WardenTaboo.NeverHunt] };
        var patched = world with { Wardens = [charter, .. world.Wardens.Skip(1)] };
        var proposal = Proposal(charter, patched.Version, WardenActionKind.Hunt);

        var (_, resolutions) = ProposalResolver.Resolve(patched, [proposal]);
        Assert.Equal(ProposalRejectionReason.TabooViolation, resolutions[0].Rejection);
    }

    [Fact]
    public void ExhaustedBudgetIsRejected()
    {
        var world = World();
        var charter = world.Wardens[0] with { ActionBudget = 0 };
        var patched = world with { Wardens = [charter, .. world.Wardens.Skip(1)] };

        var (_, resolutions) = ProposalResolver.Resolve(patched, [Proposal(charter, patched.Version)]);
        Assert.Equal(ProposalRejectionReason.InsufficientBudget, resolutions[0].Rejection);
    }

    [Fact]
    public void DuplicateIdempotencyKeysAreAppliedOnce()
    {
        var world = World();
        var charter = world.Wardens[0];
        var proposal = Proposal(charter, world.Version);

        var (_, resolutions) = ProposalResolver.Resolve(world, [proposal, proposal]);

        Assert.Equal(ProposalRejectionReason.None, resolutions[0].Rejection);
        Assert.Equal(ProposalRejectionReason.Duplicate, resolutions[1].Rejection);
    }

    [Fact]
    public void UnknownWardenIsRejected()
    {
        var world = World();
        var proposal = Proposal(world.Wardens[0], world.Version) with { WardenId = WardenId.Parse("not-a-warden") };

        var (_, resolutions) = ProposalResolver.Resolve(world, [proposal]);
        Assert.Equal(ProposalRejectionReason.UnknownWarden, resolutions[0].Rejection);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(11, 1)]
    [InlineData(1, 11)]
    [InlineData(1, -1)]
    public void OutOfRangeCostOrImpactIsRejected(int cost, int impact)
    {
        var world = World();
        var proposal = Proposal(world.Wardens[0], world.Version) with { BudgetCost = cost, Impact = impact };

        var (_, resolutions) = ProposalResolver.Resolve(world, [proposal]);
        Assert.Equal(ProposalRejectionReason.OutOfRange, resolutions[0].Rejection);
    }

    [Fact]
    public void CharterValidationRejectsCrossWorldCellIndices()
    {
        var world = World();
        var charter = world.Wardens[0] with { ControlledRegion = [world.Cells.Length + 5] };
        Assert.NotNull(charter.Validate(world.Cells.Length));
    }

    [Fact]
    public void CharterValidationRejectsUnboundedText()
    {
        var world = World();
        var charter = world.Wardens[0] with { DisplayName = new string('x', WardenCharter.MaxDisplayNameLength + 1) };
        Assert.NotNull(charter.Validate(world.Cells.Length));
    }
}
