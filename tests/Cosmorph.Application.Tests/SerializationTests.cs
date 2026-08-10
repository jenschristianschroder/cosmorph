using Cosmorph.Application.Serialization;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Tests;

/// <summary>
/// Persistence must be byte-stable and hostile documents must fail loudly rather than load
/// partially.
/// </summary>
public sealed class SerializationTests
{
    private static WorldState Create(int ticks = 5)
    {
        var state = WorldGenerator.Create(WorldId.Parse("codec-world"), "Codec World", new WorldSeed(24680UL), 32, 16);
        for (var i = 0; i < ticks; i++)
        {
            state = TickEngine.Advance(state).State;
        }

        return state with
        {
            Wardens =
            [
                new WardenCharter
                {
                    Id = WardenId.Parse("moss-keeper"),
                    DisplayName = "Moss Keeper <script>",
                    ControlledSpecies = new SpeciesId("verdant-moss"),
                    ControlledRegion = [0, 3, 9],
                    Goals = [WardenGoal.Preserve, WardenGoal.Expand],
                    GoalWeights = [60, 40],
                    Taboos = [WardenTaboo.NeverBurn],
                    ActionBudget = 12,
                    BudgetRenewalPerChapter = 6,
                    ImpactCeilingPerChapter = 25,
                    ImpactUsedThisChapter = 3,
                },
            ],
        };
    }

    [Fact]
    public void SnapshotsRoundTripWithoutLosingAuthoritativeValues()
    {
        var state = Create();

        var restored = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(
            CanonicalJson.Serialize(SnapshotCodec.ToDocument(state))));

        Assert.Equal(state.Id, restored.Id);
        Assert.Equal(state.Tick, restored.Tick);
        Assert.Equal(state.Version, restored.Version);
        Assert.Equal(state.Chapter, restored.Chapter);
        Assert.Equal(state.LastEventSequence, restored.LastEventSequence);
        Assert.Equal(state.Health.Value, restored.Health.Value);
        Assert.Equal(state.Cells.Length, restored.Cells.Length);
        Assert.Equal(
            state.Cells.Select(c => (c.Index, c.Biome, c.Elevation, c.TemperatureDeciC, c.Moisture.Value, c.Biomass)),
            restored.Cells.Select(c => (c.Index, c.Biome, c.Elevation, c.TemperatureDeciC, c.Moisture.Value, c.Biomass)));
        Assert.Equal(
            state.Populations.Select(p => (p.Species.Value, p.CellIndex, p.Population, p.Energy)),
            restored.Populations.Select(p => (p.Species.Value, p.CellIndex, p.Population, p.Energy)));
        Assert.Equal(state.Wardens.Single().DisplayName, restored.Wardens.Single().DisplayName);
        Assert.Equal(state.Wardens.Single().ControlledRegion, restored.Wardens.Single().ControlledRegion);
    }

    [Fact]
    public void SerializationIsByteStable()
    {
        var state = Create();

        var first = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));
        var second = CanonicalJson.Serialize(SnapshotCodec.ToDocument(
            SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(first))));

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownDocumentMembersAreRejected()
    {
        var json = CanonicalJson.Serialize(SnapshotCodec.ToDocument(Create(1)));
        var hostile = json.Insert(1, "\"injectedMember\":true,");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CanonicalJson.Deserialize<SnapshotDocument>(hostile));
    }

    [Fact]
    public void DeeplyNestedDocumentsAreRejected()
    {
        var depth = 200;
        var hostile = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CanonicalJson.Deserialize<SnapshotDocument>(hostile));
    }

    [Fact]
    public void EventsRoundTripThroughTheirDocument()
    {
        var worldEvent = new WorldEvent
        {
            Sequence = 42,
            Tick = 17,
            Type = WorldEventType.WorldmindDecision,
            Chapter = new ChapterId(2),
            CellIndex = 9,
            Species = new SpeciesId("verdant-moss"),
            Magnitude = -12,
            Narration = "The rains fail.",
            SimulationVersion = WorldState.CurrentSimulationVersion,
            ContentVersion = Domain.Content.ContentPack.Season1.Version,
        };

        var restored = EventCodec.ToEvent(EventCodec.ToDocument(worldEvent));

        Assert.Equal(worldEvent, restored);
    }

    [Fact]
    public void EventDocumentsFromAnUnknownSchemaAreRefused()
    {
        var document = EventCodec.ToDocument(new WorldEvent
        {
            Sequence = 1,
            Tick = 1,
            Type = WorldEventType.TimeCompressed,
            Chapter = new ChapterId(1),
            Magnitude = 1,
            SimulationVersion = WorldState.CurrentSimulationVersion,
            ContentVersion = Domain.Content.ContentPack.Season1.Version,
        }) with
        {
            Schema = "world-event/999",
        };

        Assert.Throws<InvalidOperationException>(() => EventCodec.ToEvent(document));
    }

    [Fact]
    public void WorldIdentifiersAreValidatedWhenDocumentsAreRead()
    {
        var json = CanonicalJson.Serialize(SnapshotCodec.ToDocument(Create(1)))
            .Replace("\"worldId\":\"codec-world\"", "\"worldId\":\"../../etc/passwd\"", StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(json)));
    }
}
