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
}
