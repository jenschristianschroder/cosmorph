using Cosmorph.Application.Scheduling;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Tests;

/// <summary>
/// One scheduled execution drains a bounded number of buckets and worlds, records a watermark and
/// leaves remaining work for the next execution.
/// </summary>
public sealed class TickRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 8, 30, 0, TimeSpan.Zero);

    private sealed record Harness(TestWorldStore Store, TestSchedule Schedule, FakeClock Clock, TickRunner Runner);

    private static async Task<Harness> CreateAsync(int worldCount, SimulationOptions? options = null)
    {
        var store = new TestWorldStore();
        var schedule = new TestSchedule();
        var clock = new FakeClock(Start);
        var simulation = options ?? new SimulationOptions();
        var factory = new WorldFactory(store, schedule, clock);
        for (var i = 0; i < worldCount; i++)
        {
            await factory.CreateAsync(
                WorldId.Parse($"runner-world-{i:D2}"),
                $"Runner World {i}",
                new WorldSeed(1000UL + (ulong)i),
                isPublic: true,
                "test-owner",
                CancellationToken.None);
        }

        // A persisted watermark is what lets an outage be caught up; start one minute behind creation.
        await schedule.SetWatermarkAsync(Start.AddMinutes(-1), CancellationToken.None);

        var advancer = new WorldAdvancer(store, schedule, new FakeGameMaster(), clock, simulation);
        return new Harness(store, schedule, clock, new TickRunner(schedule, advancer, clock, simulation));
    }

    [Fact]
    public void BucketsAreWholeMinutesInUtc()
    {
        var bucket = TickRunner.Bucket(new DateTimeOffset(2026, 3, 4, 10, 15, 42, 500, TimeSpan.FromHours(2)));

        Assert.Equal(new DateTimeOffset(2026, 3, 4, 8, 15, 0, TimeSpan.Zero), bucket);
    }

    [Fact]
    public async Task ARunAdvancesEveryScheduledWorldAndRecordsAWatermark()
    {
        var harness = await CreateAsync(3);
        harness.Clock.Advance(TimeSpan.FromMinutes(4));

        var summary = await harness.Runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, summary.WorldsAdvanced);
        Assert.Equal(0, summary.WorldsSkipped);
        Assert.True(summary.DirectTicks > 0);
        Assert.Equal(summary.Watermark, harness.Schedule.Watermark);
        for (var i = 0; i < 3; i++)
        {
            var manifest = (await harness.Store.TryGetManifestAsync(WorldId.Parse($"runner-world-{i:D2}"), CancellationToken.None))!.Value.Manifest;
            Assert.Equal(4, manifest.Tick);
        }
    }

    [Fact]
    public async Task WorldCountPerRunIsBounded()
    {
        var options = new SimulationOptions { MaxWorldsPerRun = 2 };
        var harness = await CreateAsync(5, options);
        harness.Clock.Advance(TimeSpan.FromMinutes(3));

        var summary = await harness.Runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, summary.WorldsAdvanced + summary.WorldsSkipped);
    }

    [Fact]
    public async Task BucketCountPerRunIsBounded()
    {
        var options = new SimulationOptions { MaxBucketsPerRun = 2 };
        var harness = await CreateAsync(1, options);
        harness.Clock.Advance(TimeSpan.FromMinutes(30));

        var summary = await harness.Runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, summary.BucketsProcessed);
    }

    [Fact]
    public async Task ASecondRunResumesFromTheWatermarkWithoutRepeatingWork()
    {
        var harness = await CreateAsync(1);
        harness.Clock.Advance(TimeSpan.FromMinutes(6));
        var first = await harness.Runner.RunOnceAsync(CancellationToken.None);

        var second = await harness.Runner.RunOnceAsync(CancellationToken.None);

        Assert.True(second.Watermark >= first.Watermark);
        Assert.Equal(0, second.DirectTicks);
        var manifest = (await harness.Store.TryGetManifestAsync(WorldId.Parse("runner-world-00"), CancellationToken.None))!.Value.Manifest;
        Assert.Equal(6, manifest.Tick);
    }

    [Fact]
    public async Task EachWorldIsAdvancedAtMostOncePerRun()
    {
        var harness = await CreateAsync(1);
        var worldId = WorldId.Parse("runner-world-00");
        harness.Clock.Advance(TimeSpan.FromMinutes(2));

        // The same world is due in several buckets; a run must still advance it only once.
        await harness.Schedule.MarkDueAsync(worldId, harness.Clock.UtcNow, CancellationToken.None);
        await harness.Schedule.MarkDueAsync(worldId, harness.Clock.UtcNow.AddMinutes(-1), CancellationToken.None);

        var summary = await harness.Runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, summary.WorldsAdvanced + summary.WorldsSkipped);
        Assert.Equal(2, summary.DirectTicks);
    }

    [Fact]
    public async Task CancellationIsObserved()
    {
        var harness = await CreateAsync(1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Runner.RunOnceAsync(cancellation.Token));
    }

    [Fact]
    public async Task OutOfRangeSimulationOptionsAreRefusedBeforeAnyWork()
    {
        var options = new SimulationOptions { MaxWorldsPerRun = 0 };
        var harness = await CreateAsync(1);
        var runner = new TickRunner(
            harness.Schedule,
            new WorldAdvancer(harness.Store, harness.Schedule, new FakeGameMaster(), harness.Clock, options),
            harness.Clock,
            options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunOnceAsync(CancellationToken.None));
    }
}
