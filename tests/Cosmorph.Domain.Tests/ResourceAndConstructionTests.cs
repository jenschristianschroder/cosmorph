using System.Collections.Immutable;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Tests;

/// <summary>
/// Materials and constructions: what a cell produces, what it may hoard, what a Warden may raise with
/// it, and what happens when a structure is left to weather away.
/// </summary>
public sealed class ResourceAndConstructionTests
{
    private static WorldState World(string worldId = "material-world", ulong seed = 3141UL) =>
        WorldGenerator.Create(WorldId.Parse(worldId), "Material World", new WorldSeed(seed), 32, 16);

    private static WorldState Run(WorldState state, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            state = TickEngine.Advance(state).State;
        }

        return state;
    }

    private static WorldState WithCharter(WorldState state, int cellIndex, WardenGoal goal = WardenGoal.Preserve)
    {
        var species = state.Populations.First(p => p.CellIndex == cellIndex).Species;
        var charter = new WardenCharter
        {
            Id = WardenId.Parse("builder"),
            DisplayName = "Builder",
            ControlledSpecies = species,
            ControlledRegion = [cellIndex],
            Goals = [goal],
            GoalWeights = [80],
            Taboos = [],
            ActionBudget = 20,
            BudgetRenewalPerChapter = 10,
            ImpactCeilingPerChapter = 40,
            ImpactUsedThisChapter = 0,
        };

        return state with { Wardens = [charter] };
    }

    /// <summary>A land cell that a species lives on, so a charter over it is a charter over something.</summary>
    private static int InhabitedLandCell(WorldState state) =>
        state.Populations.First(p => state.Cells[p.CellIndex].IsLand).CellIndex;

    private static WorldState WithStock(WorldState state, int cellIndex, CellResources stock)
    {
        var cells = state.Cells.ToArray();
        cells[cellIndex] = cells[cellIndex] with { Resources = stock };
        return state with { Cells = [.. cells] };
    }

    private static WardenProposal BuildProposal(WorldState state, ConstructionKind kind)
    {
        var charter = state.Wardens[0];
        return new WardenProposal
        {
            WardenId = charter.Id,
            Action = WardenActionKind.Build,
            TargetCellIndex = charter.ControlledRegion[0],
            TargetSpecies = charter.ControlledSpecies,
            ExpectedWorldVersion = state.Version,
            BudgetCost = 1,
            Impact = 1,
            IdempotencyKey = $"build-{kind}",
            Construction = kind,
        };
    }

    [Fact]
    public void ResourceAccrualIsDeterministicForASeed()
    {
        var first = Run(World(seed: 777UL), 30);
        var second = Run(World(seed: 777UL), 30);

        Assert.Equal(
            first.Cells.Select(c => c.Resources).ToArray(),
            second.Cells.Select(c => c.Resources).ToArray());
    }

    [Fact]
    public void LivingLandAccumulatesMaterialsAndWaterDoesNot()
    {
        var state = Run(World(), 40);

        Assert.Contains(state.Cells, c => c.IsLand && c.Resources.Total > 0);
        Assert.All(state.Cells.Where(c => !c.IsLand), c => Assert.Equal(CellResources.None, c.Resources));
    }

    [Fact]
    public void StockNeverPassesTheCeiling()
    {
        // Long enough that any unbounded cell would have run far past the ceiling.
        var state = Run(World(seed: 8080UL), 300);

        Assert.All(state.Cells, cell =>
        {
            Assert.InRange(cell.Resources.Timber, 0, CellResources.MaxStock);
            Assert.InRange(cell.Resources.Stone, 0, CellResources.MaxStock);
            Assert.InRange(cell.Resources.Fibre, 0, CellResources.MaxStock);
        });
    }

    [Fact]
    public void AddClampsAtTheCeilingRatherThanOverflowing()
    {
        var full = new CellResources(CellResources.MaxStock, CellResources.MaxStock, CellResources.MaxStock);
        var added = full.Add(new CellResources(500, 500, 500));

        Assert.Equal(full, added);
        Assert.Equal(1000, added.Richness.Value);
    }

    [Fact]
    public void SpendingIsAllOrNothing()
    {
        var stock = new CellResources(40, 10, 5);

        Assert.False(stock.TrySpend(new CellResources(40, 10, 6), out var refused));
        Assert.Equal(stock, refused);

        Assert.True(stock.TrySpend(new CellResources(40, 10, 5), out var remaining));
        Assert.Equal(CellResources.None, remaining);
    }

    [Fact]
    public void BuildIsRefusedWhenTheCellCannotPayForIt()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var state = WithStock(WithCharter(world, cellIndex), cellIndex, CellResources.None);

        var (after, resolutions) = ProposalResolver.Resolve(state, [BuildProposal(state, ConstructionKind.Shelter)]);

        Assert.Equal(ProposalRejectionReason.InsufficientResources, resolutions[0].Rejection);
        Assert.Empty(after.Constructions);
        Assert.Equal(state.Wardens[0].ActionBudget, after.Wardens[0].ActionBudget);
    }

    [Fact]
    public void BuildSpendsTheCellsOwnMaterials()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var stock = new CellResources(200, 200, 200);
        var state = WithStock(WithCharter(world, cellIndex), cellIndex, stock);

        var (after, resolutions) = ProposalResolver.Resolve(state, [BuildProposal(state, ConstructionKind.Shelter)]);

        Assert.Equal(ProposalRejectionReason.None, resolutions[0].Rejection);
        var raised = Assert.Single(after.Constructions);
        Assert.Equal(ConstructionKind.Shelter, raised.Kind);
        Assert.Equal(1, raised.Level);
        Assert.Equal(cellIndex, raised.CellIndex);

        var cost = Construction.CostFor(ConstructionKind.Shelter, 1);
        Assert.Equal(
            new CellResources(stock.Timber - cost.Timber, stock.Stone - cost.Stone, stock.Fibre - cost.Fibre),
            after.Cells[cellIndex].Resources);
    }

    [Fact]
    public void ACellHoldsOnlyOneKindOfStructure()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var state = WithStock(WithCharter(world, cellIndex), cellIndex, new CellResources(500, 500, 500)) with
        {
            Constructions = [Construction.Raise(cellIndex, ConstructionKind.Shelter)],
        };

        var (after, resolutions) = ProposalResolver.Resolve(state, [BuildProposal(state, ConstructionKind.Terrace)]);

        Assert.Equal(ProposalRejectionReason.OutOfRange, resolutions[0].Rejection);
        Assert.Equal(ConstructionKind.Shelter, Assert.Single(after.Constructions).Kind);
    }

    [Fact]
    public void BuildingOnAStructureAlreadyAtItsHighestLevelIsRefused()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var topped = new Construction(cellIndex, ConstructionKind.Shelter, Construction.MaxLevel, Permille.Full);
        var state = WithStock(WithCharter(world, cellIndex), cellIndex, new CellResources(500, 500, 500)) with
        {
            Constructions = [topped],
        };

        var (_, resolutions) = ProposalResolver.Resolve(state, [BuildProposal(state, ConstructionKind.Shelter)]);

        Assert.Equal(ProposalRejectionReason.OutOfRange, resolutions[0].Rejection);
    }

    [Fact]
    public void AnExhaustedBudgetStopsABuildTheCellCouldOtherwiseAfford()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var state = WithStock(WithCharter(world, cellIndex), cellIndex, new CellResources(500, 500, 500));
        state = state with { Wardens = [state.Wardens[0] with { ActionBudget = 0 }] };

        var (after, resolutions) = ProposalResolver.Resolve(state, [BuildProposal(state, ConstructionKind.Shelter)]);

        Assert.Equal(ProposalRejectionReason.InsufficientBudget, resolutions[0].Rejection);
        Assert.Empty(after.Constructions);
    }

    [Fact]
    public void AWardenWhoseGoalsFavourNoStructureNeverProposesOne()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);

        // Cooperate maps to no structure, so a full store of materials still buys nothing.
        var state = WithStock(WithCharter(world, cellIndex, WardenGoal.Cooperate), cellIndex, new CellResources(900, 900, 900));

        var proposals = WardenPlanner.Propose(state);

        Assert.All(proposals, p => Assert.NotEqual(WardenActionKind.Build, p.Action));
    }

    [Fact]
    public void AWardenBuildsWhatItsHighestGoalCallsFor()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var state = WithStock(WithCharter(world, cellIndex, WardenGoal.Expand), cellIndex, new CellResources(900, 900, 900));

        var proposal = Assert.Single(WardenPlanner.Propose(state));

        Assert.Equal(WardenActionKind.Build, proposal.Action);
        Assert.Equal(ConstructionKind.Terrace, proposal.Construction);
    }

    [Fact]
    public void AStructureLeftToWeatherIsRemovedAndAnnounced()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);

        // One tick from failing: the last of its condition goes this tick.
        var failing = new Construction(cellIndex, ConstructionKind.Windbreak, 1, Permille.Clamp(Construction.DecayPerTick));
        var state = world with { Constructions = [failing] };

        var (_, _, constructions, signals) = EcologyStep.Advance(state, state.Tick.Value + 1);

        Assert.Empty(constructions);
        Assert.Contains(signals, s => s.Type == WorldEventType.ConstructionLost && s.CellIndex == cellIndex);
    }

    [Fact]
    public void AStructureInGoodConditionSurvivesTheTickAndKeepsItsLevel()
    {
        var world = World();
        var cellIndex = InhabitedLandCell(world);
        var state = world with { Constructions = [new Construction(cellIndex, ConstructionKind.Terrace, 2, Permille.Full)] };

        var (_, _, constructions, signals) = EcologyStep.Advance(state, state.Tick.Value + 1);

        var standing = Assert.Single(constructions);
        Assert.Equal(2, standing.Level);
        Assert.Equal(1000 - Construction.DecayPerTick, standing.Condition.Value);
        Assert.DoesNotContain(signals, s => s.Type == WorldEventType.ConstructionLost);
    }

    [Fact]
    public void ATerraceLiftsCarryingCapacityWithinItsDeclaredBound()
    {
        var world = World();
        var cellIndex = world.Cells.First(c => c.IsLand && c.CarryingCapacity > 1000).Index;
        var bare = EcologyStep.Advance(world, world.Tick.Value + 1).Cells[cellIndex];

        var terraced = world with { Constructions = [new Construction(cellIndex, ConstructionKind.Terrace, 3, Permille.Full)] };
        var lifted = EcologyStep.Advance(terraced, terraced.Tick.Value + 1).Cells[cellIndex];

        Assert.True(
            lifted.CarryingCapacity > bare.CarryingCapacity,
            $"A terrace did not lift capacity. bare={bare.CarryingCapacity} terraced={lifted.CarryingCapacity}");

        // The bonus is 40 permille a level, so three levels at full condition is 12 percent and no more.
        Assert.InRange(
            lifted.CarryingCapacity,
            bare.CarryingCapacity,
            bare.CarryingCapacity + (bare.CarryingCapacity * 120 / 1000) + 1);
    }

    [Theory]
    [InlineData(ConstructionKind.Shelter, 60)]
    [InlineData(ConstructionKind.Terrace, 120)]
    [InlineData(ConstructionKind.Windbreak, 300)]
    public void NoEffectPassesItsCeilingAtFullLevelAndCondition(ConstructionKind kind, int ceiling)
    {
        var best = new Construction(0, kind, Construction.MaxLevel, Permille.Full);
        var effect = Math.Max(best.CapacityBonusPermille, Math.Max(best.StrainReliefPermille, best.WeatherReliefPermille));

        Assert.Equal(ceiling, effect);
        Assert.InRange(effect, 0, 1000);
    }

    [Fact]
    public void AWorldWithoutConstructionsTicksExactlyAsItDidBefore()
    {
        // The new machinery stays inert until something is built, so an existing world does not shift.
        var withEmpty = World(seed: 55UL) with { Constructions = ImmutableArray<Construction>.Empty };
        var plain = World(seed: 55UL);

        var a = Run(withEmpty, 20);
        var b = Run(plain, 20);

        Assert.Equal(a.Cells.ToArray(), b.Cells.ToArray());
        Assert.Equal(a.Health.Value, b.Health.Value);
    }
}
