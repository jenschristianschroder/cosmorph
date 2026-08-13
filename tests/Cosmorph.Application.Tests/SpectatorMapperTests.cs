using Cosmorph.Application.Spectator;
using Cosmorph.Application.Serialization;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Tests;

/// <summary>
/// Spectator read models are presentation-only. Private Warden configuration, rationale, prompts,
/// storage paths and actor identities are never projected.
/// </summary>
public sealed class SpectatorMapperTests
{
    private static WorldState Create()
    {
        var state = WorldGenerator.Create(WorldId.Parse("spectator-world"), "Spectator World", new WorldSeed(555UL), 32, 16);
        state = TickEngine.Advance(state).State;
        return state with
        {
            Wardens =
            [
                new WardenCharter
                {
                    Id = WardenId.Parse("moss-keeper"),
                    DisplayName = "Secret Keeper",
                    ControlledSpecies = new SpeciesId("verdant-moss"),
                    ControlledRegion = [1, 2, 3],
                    Goals = [WardenGoal.Preserve],
                    GoalWeights = [100],
                    Taboos = [WardenTaboo.NeverBurn],
                    ActionBudget = 11,
                    BudgetRenewalPerChapter = 7,
                    ImpactCeilingPerChapter = 33,
                    ImpactUsedThisChapter = 2,
                },
            ],
        };
    }

    [Fact]
    public void SummaryExposesOnlyPublicPresentationValues()
    {
        var state = Create();

        var summary = SpectatorMapper.ToSummary(state, DateTimeOffset.UnixEpoch, "FakeGameMaster");
        var json = CanonicalJson.Serialize(summary);

        Assert.Equal(state.Id.Value, summary.WorldId);
        Assert.Equal("FakeGameMaster", summary.WorldmindMode);
        Assert.DoesNotContain("Secret Keeper", json, StringComparison.Ordinal);
        Assert.DoesNotContain("moss-keeper", json, StringComparison.Ordinal);
        Assert.DoesNotContain("actionBudget", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotContainsNoWardenConfigurationOrStorageDetail()
    {
        var state = Create();

        var json = CanonicalJson.Serialize(SpectatorMapper.ToSnapshot(state));

        Assert.DoesNotContain("Secret Keeper", json, StringComparison.Ordinal);
        Assert.DoesNotContain("warden", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taboo", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotCellArraysMatchTheGridAndStayWithinDisplayRanges()
    {
        var state = Create();

        var snapshot = SpectatorMapper.ToSnapshot(state);

        var count = state.GridWidth * state.GridHeight;
        Assert.Equal(count, snapshot.Cells.Biome.Length);
        Assert.Equal(count, snapshot.Cells.VitalityPermille.Length);
        Assert.Equal(count, snapshot.Cells.PopulationPressurePermille.Length);
        Assert.All(snapshot.Cells.VitalityPermille, value => Assert.InRange(value, 0, 1000));
        Assert.All(snapshot.Cells.StressPermille, value => Assert.InRange(value, 0, 1000));
        Assert.All(snapshot.Cells.PopulationPressurePermille, value => Assert.InRange(value, 0, 1000));
        Assert.Equal(SpectatorSnapshotDto.CurrentSchema, snapshot.Schema);
    }

    [Fact]
    public void SpeciesTotalsAreOrderedAndComplete()
    {
        var state = Create();

        var snapshot = SpectatorMapper.ToSnapshot(state);

        Assert.NotEmpty(snapshot.Species);
        Assert.Equal(snapshot.Species.Select(s => s.Species).Order(StringComparer.Ordinal), snapshot.Species.Select(s => s.Species));
        Assert.Equal(
            state.Populations.Sum(p => (long)p.Population),
            snapshot.Species.Sum(s => s.Population));
    }

    [Fact]
    public void EventsAreProjectedWithCoordinatesInsideTheGrid()
    {
        var worldEvent = new WorldEvent
        {
            Sequence = 3,
            Tick = 9,
            Type = WorldEventType.Drought,
            Chapter = new ChapterId(1),
            CellIndex = 40,
            Species = new SpeciesId("verdant-moss"),
            Magnitude = 25,
            Narration = null,
            SimulationVersion = WorldState.CurrentSimulationVersion,
            ContentVersion = Domain.Content.ContentPack.Season1.Version,
        };

        var dto = SpectatorMapper.ToEvent(worldEvent, 32, 16);

        Assert.Equal("Drought", dto.Type);
        Assert.InRange(dto.LatitudeDegrees!.Value, -90, 90);
        Assert.InRange(dto.LongitudeDegrees!.Value, -180, 180);
    }

    [Fact]
    public void EventsWithAnOutOfRangeCellIndexCarryNoCoordinates()
    {
        var worldEvent = new WorldEvent
        {
            Sequence = 4,
            Tick = 9,
            Type = WorldEventType.Fire,
            Chapter = new ChapterId(1),
            CellIndex = 100_000,
            Magnitude = 1,
            SimulationVersion = WorldState.CurrentSimulationVersion,
            ContentVersion = Domain.Content.ContentPack.Season1.Version,
        };

        var dto = SpectatorMapper.ToEvent(worldEvent, 32, 16);

        Assert.Null(dto.LatitudeDegrees);
        Assert.Null(dto.LongitudeDegrees);
    }

    [Fact]
    public void ANeighbourhoodIsTheBlockAroundItsCentre()
    {
        var state = Create();

        // Row 5, column 10 of a 32 by 16 grid: away from both poles and both edges.
        var block = SpectatorMapper.ToNeighbourhood(state, (5 * 32) + 10, 1);

        Assert.NotNull(block);
        Assert.Equal(NeighbourhoodDto.CurrentSchema, block!.Schema);
        Assert.Equal(3, block.Rows);
        Assert.Equal(3, block.Columns);
        Assert.Equal(9, block.Cells.Length);
        Assert.Equal((5 * 32) + 10, block.CenterCellIndex);

        // Row-major, so the middle of a three-by-three block is the centre it was asked for.
        Assert.Equal(block.CenterCellIndex, block.Cells[4].CellIndex);
        Assert.Equal(
            new[] { 4 * 32 + 9, 4 * 32 + 10, 4 * 32 + 11, 5 * 32 + 9, 5 * 32 + 10, 5 * 32 + 11, 6 * 32 + 9, 6 * 32 + 10, 6 * 32 + 11 },
            block.Cells.Select(c => c.CellIndex));
    }

    [Fact]
    public void ANeighbourhoodAtThePoleIsClippedRatherThanWrapped()
    {
        var state = Create();

        // The grid is a cylinder, not a torus: there is no row above the top one to fold onto.
        var block = SpectatorMapper.ToNeighbourhood(state, 10, 1);

        Assert.NotNull(block);
        Assert.Equal(2, block!.Rows);
        Assert.Equal(3, block.Columns);
        Assert.Equal(6, block.Cells.Length);
        Assert.All(block.Cells, cell => Assert.InRange(cell.CellIndex, 0, (state.GridWidth * 2) - 1));
    }

    [Fact]
    public void ANeighbourhoodAcrossTheDateLineWrapsColumns()
    {
        var state = Create();
        var lastColumn = state.GridWidth - 1;

        var block = SpectatorMapper.ToNeighbourhood(state, 5 * state.GridWidth, 1);

        Assert.NotNull(block);
        Assert.Contains(block!.Cells, cell => cell.CellIndex == (5 * state.GridWidth) + lastColumn);
        Assert.Equal(9, block.Cells.Length);
        Assert.Equal(block.Cells.Length, block.Cells.Select(c => c.CellIndex).Distinct().Count());
    }

    [Fact]
    public void ANeighbourhoodOfNoReachIsTheCentreAlone()
    {
        var state = Create();

        var block = SpectatorMapper.ToNeighbourhood(state, 100, 0);

        Assert.NotNull(block);
        Assert.Equal(1, block!.Rows);
        Assert.Equal(1, block.Columns);
        Assert.Equal(100, Assert.Single(block.Cells).CellIndex);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(512, 1)]
    [InlineData(100, 3)]
    [InlineData(100, -1)]
    public void ANeighbourhoodOutsideWhatIsOfferedIsNothing(int cellIndex, int radius)
    {
        var state = Create();

        Assert.Null(SpectatorMapper.ToNeighbourhood(state, cellIndex, radius));
    }

    [Fact]
    public void EveryPlaceInANeighbourhoodIsTheSameProjectionAsReadingItAlone()
    {
        var state = Create();

        var block = SpectatorMapper.ToNeighbourhood(state, (7 * 32) + 4, 2);

        Assert.NotNull(block);
        Assert.All(block!.Cells, cell =>
            Assert.Equal(
                CanonicalJson.Serialize(SpectatorMapper.ToCellDetail(state, cell.CellIndex)),
                CanonicalJson.Serialize(cell)));
    }

    [Fact]
    public void ANeighbourhoodCarriesNoWardenConfiguration()
    {
        var state = Create();

        var json = CanonicalJson.Serialize(SpectatorMapper.ToNeighbourhood(state, 40, 2));

        Assert.DoesNotContain("Secret Keeper", json, StringComparison.Ordinal);
        Assert.DoesNotContain("warden", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", json, StringComparison.OrdinalIgnoreCase);
    }
}
