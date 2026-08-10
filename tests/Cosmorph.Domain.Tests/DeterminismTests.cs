using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Ticking;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Tests;

/// <summary>
/// The core promise: the same seed and inputs produce the same canonical state and Chronicle, and
/// two worlds never influence each other.
/// </summary>
public sealed class DeterminismTests
{
    private static WorldState Create(string worldId, ulong seed) =>
        WorldGenerator.Create(WorldId.Parse(worldId), "Test World", new WorldSeed(seed), 32, 16);

    private static (WorldState State, List<WorldEvent> Events) Run(string worldId, ulong seed, int ticks)
    {
        var state = Create(worldId, seed);
        var events = new List<WorldEvent>();
        for (var i = 0; i < ticks; i++)
        {
            var outcome = TickEngine.Advance(state);
            state = outcome.State;
            events.AddRange(outcome.Events);
        }

        return (state, events);
    }

    [Fact]
    public void SameSeedProducesIdenticalStateAndChronicle()
    {
        var (firstState, firstEvents) = Run("determinism-world", 987654321UL, 120);
        var (secondState, secondEvents) = Run("determinism-world", 987654321UL, 120);

        var firstFingerprint = Fingerprint(firstState);
        var secondFingerprint = Fingerprint(secondState);

        Assert.True(
            firstFingerprint == secondFingerprint,
            $"Canonical state diverged. seed=987654321 tick={firstState.Tick.Value} version={firstState.Version} health={firstState.Health.Value}");
        Assert.Equal(firstEvents.Count, secondEvents.Count);
        Assert.Equal(
            firstEvents.Select(e => (e.Sequence, e.Type, e.Tick, e.Magnitude)),
            secondEvents.Select(e => (e.Sequence, e.Type, e.Tick, e.Magnitude)));
    }

    /// <summary>Order-sensitive fingerprint of every authoritative value of a world.</summary>
    private static string Fingerprint(WorldState state)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(state.Tick.Value).Append('|')
            .Append(state.Version).Append('|')
            .Append(state.Chapter.Value).Append('|')
            .Append(state.Season.Value).Append('|')
            .Append(state.LastEventSequence).Append('|')
            .Append(state.Health.Value).Append(';');

        foreach (var cell in state.Cells)
        {
            builder.Append(cell.Index).Append(',')
                .Append((int)cell.Biome).Append(',')
                .Append(cell.Elevation).Append(',')
                .Append(cell.TemperatureDeciC).Append(',')
                .Append(cell.Moisture.Value).Append(',')
                .Append(cell.Biomass).Append(',')
                .Append(cell.CarryingCapacity).Append(',')
                .Append(cell.Stress.Drought.Value).Append(',')
                .Append(cell.Stress.Disease.Value).Append(',')
                .Append(cell.Stress.Fire.Value).Append(',')
                .Append(cell.Stress.Flood.Value).Append(';');
        }

        foreach (var population in state.Populations)
        {
            builder.Append(population.Species.Value).Append(',')
                .Append(population.CellIndex).Append(',')
                .Append(population.Population).Append(',')
                .Append(population.Energy).Append(',')
                .Append(population.Health.Value).Append(',')
                .Append(population.Traits.ColdTolerance).Append(',')
                .Append(population.Traits.DroughtTolerance).Append(';');
        }

        return builder.ToString();
    }

    [Fact]
    public void DifferentSeedsDivergeVisibly()
    {
        var (first, _) = Run("world-alpha", 12345UL, 60);
        var (second, _) = Run("world-beta", 6543210UL, 60);

        var differentCells = first.Cells
            .Zip(second.Cells, (a, b) => a.Biome != b.Biome || a.Vitality != b.Vitality)
            .Count(different => different);

        Assert.True(
            differentCells > first.Cells.Length / 10,
            $"Worlds did not diverge. differing={differentCells} of {first.Cells.Length}");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(4242UL)]
    [InlineData(ulong.MaxValue)]
    public void InvariantsHoldAcrossManySeedsAndTicks(ulong seed)
    {
        var state = Create("invariant-world", seed);
        for (var tick = 0; tick < 80; tick++)
        {
            var outcome = TickEngine.Advance(state);
            state = outcome.State;

            Assert.Equal(tick + 1, state.Tick.Value);
            foreach (var cell in state.Cells)
            {
                Assert.InRange(cell.Vitality.Value, 0, 1000);
                Assert.InRange(cell.Moisture.Value, 0, 1000);
                Assert.InRange(cell.Biomass, 0, int.MaxValue);
                Assert.InRange(cell.Stress.Drought.Value, 0, 1000);
            }

            foreach (var population in state.Populations)
            {
                Assert.True(
                    population.Population >= 0,
                    $"Negative population. seed={seed} tick={state.Tick.Value} version={state.Version} species={population.Species.Value}");
            }

            Assert.InRange(state.Health.Value, 0, 1000);
            Assert.True(
                outcome.Events.Length <= TickEngine.MaxTotalEventsPerTick(state),
                $"Too many events in one tick. seed={seed} tick={state.Tick.Value} events={outcome.Events.Length}");
        }
    }

    [Fact]
    public void EventSequencesAreStrictlyIncreasing()
    {
        var (state, events) = Run("sequence-world", 55UL, 90);
        var sequences = events.Select(e => e.Sequence).ToArray();

        Assert.Equal(sequences.OrderBy(s => s).ToArray(), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Length);
        Assert.Equal(state.LastEventSequence, sequences.Length == 0 ? 0 : sequences[^1]);
    }

    [Fact]
    public void CompressedCatchUpAdvancesTimeAndRecordsAnEvent()
    {
        var state = Create("compress-world", 4242UL);
        var (compressed, events) = TickEngine.Compress(state, 500);

        Assert.Equal(500, compressed.Tick.Value);
        Assert.Contains(events, e => e.Type == WorldEventType.TimeCompressed);
        Assert.All(compressed.Cells, cell => Assert.InRange(cell.Vitality.Value, 0, 1000));
    }

    [Fact]
    public void CompressionIsBoundedPerCall()
    {
        var state = Create("compress-bound", 99UL);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TickEngine.Compress(state, TickEngine.MaxCompressedTicks + 1));
    }
}
