using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Worlds;
using Cosmorph.Infrastructure.Files;
using Cosmorph.Infrastructure.Memory;

namespace Cosmorph.Infrastructure.Tests;

/// <summary>
/// One contract, run against every store implementation that can be exercised without Azure. The
/// Blob implementation follows the same contract and is covered by an opt-in environment.
/// </summary>
public sealed class WorldStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cosmorph-tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Implementations => new("memory", "filesystem");

    private IWorldStore Create(string implementation) => implementation switch
    {
        "memory" => new InMemoryWorldStore(),
        "filesystem" => new FileSystemWorldStore(_root),
        _ => throw new ArgumentOutOfRangeException(nameof(implementation)),
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static WorldState State(string worldId, ulong seed = 99UL) =>
        WorldGenerator.Create(WorldId.Parse(worldId), "Store World", new WorldSeed(seed), 16, 8);

    private static WorldManifest Manifest(WorldState state, bool isPublic = true) => new()
    {
        Id = state.Id,
        Name = state.Name,
        Seed = state.Seed.Value,
        Tick = state.Tick.Value,
        Version = state.Version,
        Chapter = state.Chapter.Value,
        LastEventSequence = state.LastEventSequence,
        LastAdvancedAtUtc = DateTimeOffset.UnixEpoch,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        IsPaused = false,
        IsPublic = isPublic,
        GridWidth = state.GridWidth,
        GridHeight = state.GridHeight,
        SimulationVersion = state.SimulationVersion,
        ContentVersion = state.ContentVersion,
    };

    private static WorldEvent Event(long sequence) => new()
    {
        Sequence = sequence,
        Tick = sequence,
        Type = WorldEventType.Drought,
        Chapter = new ChapterId(1),
        CellIndex = 3,
        Magnitude = 10,
        SimulationVersion = WorldState.CurrentSimulationVersion,
        ContentVersion = Domain.Content.ContentPack.Season1.Version,
    };

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AWorldIsCreatedOnlyOnce(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");

        Assert.True(await store.TryCreateAsync(state, Manifest(state), CancellationToken.None));
        Assert.False(await store.TryCreateAsync(state, Manifest(state), CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task SnapshotsAreAddressedByTickAndRoundTrip(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        var advanced = TickEngine.Advance(state).State;
        await store.WriteSnapshotAsync(advanced, CancellationToken.None);

        var loaded = await store.TryLoadSnapshotAsync(state.Id, advanced.Tick.Value, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(advanced.Version, loaded!.Version);
        Assert.Equal(advanced.Health.Value, loaded.Health.Value);
        Assert.Null(await store.TryLoadSnapshotAsync(state.Id, 99_999, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task RepeatedEventWritesNeverDuplicateHistory(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        var events = new[] { Event(1), Event(2), Event(3) };

        await store.AppendEventsAsync(state.Id, events, CancellationToken.None);
        await store.AppendEventsAsync(state.Id, events, CancellationToken.None);

        var stored = await store.ReadEventsAsync(state.Id, 0, 100, CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], stored.Select(e => e.Sequence));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task EventsArePagedByCursorAndLimit(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        await store.AppendEventsAsync(state.Id, [.. Enumerable.Range(1, 10).Select(i => Event(i))], CancellationToken.None);

        var page = await store.ReadEventsAsync(state.Id, 4, 3, CancellationToken.None);

        Assert.Equal([5L, 6L, 7L], page.Select(e => e.Sequence));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AStaleConcurrencyTokenCannotReplaceTheManifest(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        var held = (await store.TryGetManifestAsync(state.Id, CancellationToken.None))!.Value;

        Assert.True(await store.TryReplaceManifestAsync(held.Manifest with { Tick = 1 }, held.ConcurrencyToken, CancellationToken.None));
        Assert.False(await store.TryReplaceManifestAsync(held.Manifest with { Tick = 2 }, held.ConcurrencyToken, CancellationToken.None));

        var current = (await store.TryGetManifestAsync(state.Id, CancellationToken.None))!.Value.Manifest;
        Assert.Equal(1, current.Tick);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task OnlyOneLeaseHolderCanAdvanceAWorld(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);

        var first = await store.TryAcquireLeaseAsync(state.Id, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await store.TryAcquireLeaseAsync(state.Id, TimeSpan.FromSeconds(30), CancellationToken.None));

        await first!.DisposeAsync();
        var second = await store.TryAcquireLeaseAsync(state.Id, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task ADuplicateIdempotencyKeyIsRejected(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        var command = new WorldCommand
        {
            CommandId = "command-1",
            WorldId = state.Id,
            Kind = WorldCommandKind.PauseWorld,
            ActorId = "test",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };

        Assert.True(await store.TryWriteCommandAsync(command, CancellationToken.None));
        Assert.False(await store.TryWriteCommandAsync(command, CancellationToken.None));
        Assert.Single(await store.ReadPendingCommandsAsync(state.Id, 10, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AppliedCommandsAreNoLongerPending(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        await store.TryWriteCommandAsync(
            new WorldCommand
            {
                CommandId = "command-1",
                WorldId = state.Id,
                Kind = WorldCommandKind.ResumeWorld,
                ActorId = "test",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
            },
            CancellationToken.None);

        await store.MarkCommandsAppliedAsync(state.Id, ["command-1"], CancellationToken.None);

        Assert.Empty(await store.ReadPendingCommandsAsync(state.Id, 10, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task PrivateWorldsAreNeverListed(string implementation)
    {
        var store = Create(implementation);
        var publicState = State("public-world");
        var privateState = State("private-world");
        await store.TryCreateAsync(publicState, Manifest(publicState), CancellationToken.None);
        await store.TryCreateAsync(privateState, Manifest(privateState, isPublic: false), CancellationToken.None);

        var listed = await store.ListPublicWorldsAsync(CancellationToken.None);

        Assert.Equal(["public-world"], listed.Select(m => m.Id.Value));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task WorldsNeverSeeEachOthersDataOrDecisions(string implementation)
    {
        var store = Create(implementation);
        var first = State("first-world", 1UL);
        var second = State("second-world", 1UL);
        await store.TryCreateAsync(first, Manifest(first), CancellationToken.None);
        await store.TryCreateAsync(second, Manifest(second), CancellationToken.None);

        await store.AppendEventsAsync(first.Id, [Event(1)], CancellationToken.None);
        await store.WriteDecisionAuditAsync(Audit(first.Id, "shared-fingerprint"), CancellationToken.None);

        Assert.Empty(await store.ReadEventsAsync(second.Id, 0, 10, CancellationToken.None));
        Assert.Null(await store.TryGetDecisionAsync(second.Id, "shared-fingerprint", CancellationToken.None));
        Assert.NotNull(await store.TryGetDecisionAsync(first.Id, "shared-fingerprint", CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AnAcceptedDecisionCanBeReplayedByFingerprint(string implementation)
    {
        var store = Create(implementation);
        var state = State("contract-world");
        await store.TryCreateAsync(state, Manifest(state), CancellationToken.None);
        var record = Audit(state.Id, "fingerprint-1");

        await store.WriteDecisionAuditAsync(record, CancellationToken.None);
        var replayed = await store.TryGetDecisionAsync(state.Id, "fingerprint-1", CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(record.DecisionId, replayed!.DecisionId);
        Assert.Equal("endures", replayed.Decision!.SelectedCandidateId);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task AMissingWorldIsReportedAsMissing(string implementation)
    {
        var store = Create(implementation);

        Assert.Null(await store.TryGetManifestAsync(WorldId.Parse("no-such-world"), CancellationToken.None));
        Assert.Null(await store.TryLoadSnapshotAsync(WorldId.Parse("no-such-world"), 0, CancellationToken.None));
    }

    private static DecisionAuditRecord Audit(WorldId worldId, string fingerprint) => new()
    {
        DecisionId = DecisionAuditRecord.CreateId(5, fingerprint),
        WorldId = worldId,
        Tick = 5,
        WorldVersion = 5,
        Situation = SituationKind.SevereDrought,
        Fingerprint = fingerprint,
        PromptTemplateVersion = DecisionRequest.PromptTemplateVersion,
        ModelDeployment = "fake-worldmind",
        Result = DecisionValidationResult.Accepted,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        Decision = new GameMasterDecision
        {
            SelectedCandidateId = "endures",
            Ranking = [],
            Narration = "The herds endure.",
            Rationale = "test",
            IsFallback = false,
        },
    };
}
