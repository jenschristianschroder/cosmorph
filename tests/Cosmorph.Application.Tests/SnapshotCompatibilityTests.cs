using Cosmorph.Application.Serialization;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Tests;

/// <summary>
/// Compatibility of the stored snapshot across the schema move to <c>world-snapshot/2</c>. The
/// legacy document below is a literal on purpose: it is what is already sitting in storage, so it
/// must not be regenerated from the code it is meant to guard.
/// </summary>
public sealed class SnapshotCompatibilityTests
{
    /// <summary>
    /// A whole <c>world-snapshot/1</c> document on the smallest legal grid, four by three. It has no
    /// material columns and no constructions, because neither existed when it was written.
    /// </summary>
    private const string LegacyDocument = """
        {"schema":"world-snapshot/1","worldId":"legacy-world","name":"Legacy World","seed":24680,
        "simulationVersion":"sim/1.0.0","contentVersion":"season-1.0.0","topologyVersion":1,
        "gridWidth":4,"gridHeight":3,"season":1,"chapter":1,"chapterStartTick":0,"version":7,
        "tick":7,"ticksPerDay":1,"daysPerYear":360,"axialTiltDegrees":23,"isPaused":false,
        "lastEventSequence":3,"lastSignificantTick":5,
        "cells":{
          "biome":[1,0,0,1,4,5,6,9,1,0,0,1],
          "elevation":[0,0,0,0,120,200,60,1400,0,0,0,0],
          "temperatureDeciC":[-210,-40,-30,-200,150,180,260,20,-220,-50,-35,-205],
          "moisturePermille":[300,900,900,300,420,610,880,240,310,900,900,305],
          "biomass":[0,0,0,0,4200,9100,15000,300,0,0,0,0],
          "carryingCapacity":[0,0,0,0,7000,12000,20000,900,0,0,0,0],
          "drought":[0,0,0,0,120,0,0,540,0,0,0,0],
          "disease":[0,0,0,0,0,60,0,0,0,0,0,0],
          "fire":[0,0,0,0,0,0,0,0,0,0,0,0],
          "flood":[0,0,0,0,0,0,210,0,0,0,0,0]
        },
        "populations":[
          {"species":"verdant-moss","cellIndex":4,"population":1800,"energy":600,"healthPermille":720,"coldTolerance":5,"droughtTolerance":9},
          {"species":"verdant-moss","cellIndex":6,"population":4200,"energy":700,"healthPermille":810,"coldTolerance":3,"droughtTolerance":4}
        ],
        "wardens":[
          {"id":"moss-keeper","displayName":"Moss Keeper","controlledSpecies":"verdant-moss",
           "controlledRegion":[4,5,6],"goals":[0,1],"goalWeights":[60,40],"taboos":[2],
           "actionBudget":12,"budgetRenewalPerChapter":6,"impactCeilingPerChapter":25,"impactUsedThisChapter":3}
        ]}
        """;

    [Fact]
    public void AStoredLegacyDocumentStillLoads()
    {
        var state = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(LegacyDocument));

        Assert.Equal("legacy-world", state.Id.Value);
        Assert.Equal(7, state.Tick.Value);
        Assert.Equal(12, state.Cells.Length);
        Assert.Equal(Biome.Rainforest, state.Cells[6].Biome);
        Assert.Equal(2, state.Populations.Length);
        Assert.Equal("moss-keeper", state.Wardens.Single().Id.Value);
    }

    [Fact]
    public void ALegacyDocumentLoadsWithAnEmptyStoreAndNothingBuilt()
    {
        var state = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(LegacyDocument));

        Assert.All(state.Cells, cell => Assert.Equal(CellResources.None, cell.Resources));
        Assert.Empty(state.Constructions);
    }

    [Fact]
    public void ALegacyWorldFillsItsStoreOnTheNextTick()
    {
        var state = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(LegacyDocument));

        var advanced = TickEngine.Advance(state).State;

        Assert.Contains(advanced.Cells, cell => cell.Resources.Total > 0);
    }

    [Fact]
    public void ALegacyWorldIsWrittenBackUnderTheCurrentSchema()
    {
        var state = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(LegacyDocument));

        var rewritten = SnapshotCodec.ToDocument(state);

        Assert.Equal(SnapshotDocument.CurrentSchema, rewritten.Schema);
        Assert.NotNull(rewritten.Cells.Timber);
        Assert.Equal(state.Cells.Length, rewritten.Cells.Timber!.Length);
    }

    [Fact]
    public void ADocumentFromAnUnknownSchemaIsStillRefused()
    {
        var document = CanonicalJson.Deserialize<SnapshotDocument>(LegacyDocument) with
        {
            Schema = "world-snapshot/99",
        };

        Assert.Throws<InvalidOperationException>(() => SnapshotCodec.ToState(document));
    }

    [Fact]
    public void MaterialsAndConstructionsSurviveARoundTrip()
    {
        var state = WithMaterials();

        var restored = SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(
            CanonicalJson.Serialize(SnapshotCodec.ToDocument(state))));

        Assert.Equal(
            state.Cells.Select(c => c.Resources),
            restored.Cells.Select(c => c.Resources));
        // As arrays: comparing two ImmutableArray values directly compares the underlying array by
        // reference, which is never equal across a round trip however identical the contents are.
        Assert.Equal(state.Constructions.ToArray(), restored.Constructions.ToArray());
    }

    [Fact]
    public void ASnapshotCarryingMaterialsIsByteStable()
    {
        var state = WithMaterials();

        var first = CanonicalJson.Serialize(SnapshotCodec.ToDocument(state));
        var second = CanonicalJson.Serialize(SnapshotCodec.ToDocument(
            SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(first))));

        Assert.Equal(first, second);
    }

    /// <summary>A ticked world with a stock in every cell and one structure standing on it.</summary>
    private static WorldState WithMaterials()
    {
        var state = WorldGenerator.Create(WorldId.Parse("stocked-world"), "Stocked World", new WorldSeed(1357UL), 32, 16);
        for (var i = 0; i < 5; i++)
        {
            state = TickEngine.Advance(state).State;
        }

        var land = state.Cells.First(c => c.IsLand).Index;
        Assert.Equal(ContentPack.Season1.Version, state.ContentVersion);

        return state with
        {
            Constructions = [new Construction(land, ConstructionKind.Terrace, 2, new Permille(640))],
        };
    }
}
