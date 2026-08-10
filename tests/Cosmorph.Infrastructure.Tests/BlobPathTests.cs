using Cosmorph.Domain.Worlds;
using Cosmorph.Infrastructure.Blob;

namespace Cosmorph.Infrastructure.Tests;

/// <summary>
/// Every Blob path is composed from validated identifiers. A caller-supplied path, a traversal
/// attempt or an unvalidated identifier must never reach storage.
/// </summary>
public sealed class BlobPathTests
{
    [Fact]
    public void PathsAreStableAndWorldScoped()
    {
        var worldId = WorldId.Parse("verdant-cradle");

        Assert.Equal("worlds/verdant-cradle/manifest.json", BlobPaths.Manifest(worldId));
        Assert.Equal("worlds/verdant-cradle/lease", BlobPaths.Lease(worldId));
        Assert.Equal("worlds/verdant-cradle/snapshots/00000000000000000042.json.br", BlobPaths.Snapshot(worldId, 42));
        Assert.Equal("worlds/verdant-cradle/events/0000000003.jsonl.br", BlobPaths.EventSegment(worldId, 3));
        Assert.Equal("worlds/verdant-cradle/commands/abc-123.json", BlobPaths.Command(worldId, "abc-123"));
        Assert.StartsWith("worlds/verdant-cradle/", BlobPaths.CommandPrefix(worldId), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotAndSegmentIndicesAreNeverNegative()
    {
        var worldId = WorldId.Parse("verdant-cradle");

        Assert.Throws<ArgumentOutOfRangeException>(() => BlobPaths.Snapshot(worldId, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlobPaths.EventSegment(worldId, -1));
    }

    [Fact]
    public void SegmentBoundariesFollowTheSegmentSize()
    {
        Assert.Equal(0, BlobPaths.SegmentOf(1));
        Assert.Equal(0, BlobPaths.SegmentOf(BlobPaths.EventsPerSegment));
        Assert.Equal(1, BlobPaths.SegmentOf(BlobPaths.EventsPerSegment + 1));
        Assert.Equal(0, BlobPaths.SegmentOf(0));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("world/../../secret")]
    [InlineData("UPPER")]
    [InlineData("")]
    public void InvalidWorldIdentifiersAreRefused(string value)
    {
        // An unvalidated identifier can only exist as a default struct or through direct construction.
        Assert.False(WorldId.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => BlobPaths.Manifest(default));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("with/slash")]
    [InlineData("with space")]
    [InlineData("wîth-unicode")]
    public void CommandIdentifiersMayNotShapeThePath(string commandId) =>
        Assert.Throws<ArgumentException>(() => BlobPaths.Command(WorldId.Parse("verdant-cradle"), commandId));

    [Fact]
    public void OverlongCommandIdentifiersAreRefused() =>
        Assert.Throws<ArgumentException>(() => BlobPaths.Command(WorldId.Parse("verdant-cradle"), new string('a', 65)));

    [Fact]
    public void DecisionAuditPathsAreHashedAndDoNotLeakTheFingerprint()
    {
        var path = BlobPaths.DecisionAudit(WorldId.Parse("verdant-cradle"), "world:sim/1.0.0:decide:5:1:9");

        Assert.StartsWith("audit/worldmind/verdant-cradle/", path, StringComparison.Ordinal);
        Assert.DoesNotContain(':', path);
        Assert.DoesNotContain("sim/1.0.0", path, StringComparison.Ordinal);
        Assert.Equal(path, BlobPaths.DecisionAudit(WorldId.Parse("verdant-cradle"), "world:sim/1.0.0:decide:5:1:9"));
    }

    [Fact]
    public void ScheduleMarkersUseMinuteBucketsInUtc()
    {
        var bucket = new DateTimeOffset(2026, 5, 6, 9, 7, 0, TimeSpan.FromHours(2));

        var marker = BlobPaths.ScheduleMarker(bucket, 0, WorldId.Parse("verdant-cradle"));

        Assert.Equal("schedule/202605060707/00/verdant-cradle.json", marker);
        Assert.StartsWith(BlobPaths.SchedulePrefix(bucket, 0), marker, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorldIdentifierCanBeRecoveredFromAScheduleMarker() =>
        Assert.Equal(
            "verdant-cradle",
            BlobPaths.WorldIdFromMarker("schedule/202605060707/00/verdant-cradle.json"));
}
