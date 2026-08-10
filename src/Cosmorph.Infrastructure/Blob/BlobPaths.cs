using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Blob;

/// <summary>
/// Versioned Blob layout. Every path is composed from validated identifiers; a caller-supplied blob
/// path is never accepted.
/// </summary>
public static class BlobPaths
{
    public const string ContainerName = "cosmorph";

    /// <summary>Number of events stored in one immutable segment.</summary>
    public const int EventsPerSegment = 500;

    public static string Manifest(WorldId worldId) => $"worlds/{Validate(worldId)}/manifest.json";

    public static string Lease(WorldId worldId) => $"worlds/{Validate(worldId)}/lease";

    public static string Snapshot(WorldId worldId, long tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        return $"worlds/{Validate(worldId)}/snapshots/{tick.ToString("D20", CultureInfo.InvariantCulture)}.json.br";
    }

    public static string EventSegment(WorldId worldId, long segment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segment);
        return $"worlds/{Validate(worldId)}/events/{segment.ToString("D10", CultureInfo.InvariantCulture)}.jsonl.br";
    }

    public static long SegmentOf(long sequence) => Math.Max(0, sequence - 1) / EventsPerSegment;

    public static string Command(WorldId worldId, string commandId) =>
        $"worlds/{Validate(worldId)}/commands/{ValidateToken(commandId)}.json";

    public static string AppliedCommand(WorldId worldId, string commandId) =>
        $"worlds/{Validate(worldId)}/commands-applied/{ValidateToken(commandId)}.json";

    public static string CommandPrefix(WorldId worldId) => $"worlds/{Validate(worldId)}/commands/";

    public static string DecisionAudit(WorldId worldId, string fingerprint) =>
        $"audit/worldmind/{Validate(worldId)}/{FingerprintHash(fingerprint)}.json";

    public static string ScheduleMarker(DateTimeOffset bucket, int shard, WorldId worldId) =>
        $"schedule/{bucket.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}/{shard:D2}/{Validate(worldId)}.json";

    public static string SchedulePrefix(DateTimeOffset bucket, int shard) =>
        $"schedule/{bucket.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}/{shard:D2}/";

    public static string Watermark(int shard) => $"scheduler/watermark/{shard:D2}.json";

    public static string WorldIdFromMarker(string blobName)
    {
        ArgumentException.ThrowIfNullOrEmpty(blobName);
        var name = blobName[(blobName.LastIndexOf('/') + 1)..];
        return name.EndsWith(".json", StringComparison.Ordinal) ? name[..^5] : name;
    }

    private static string FingerprintHash(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return Convert.ToHexStringLower(hash);
    }

    private static string Validate(WorldId worldId) =>
        WorldId.TryParse(worldId.Value, out var validated)
            ? validated.Value
            : throw new ArgumentException("Invalid world identifier.", nameof(worldId));

    private static string ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (token.Length > 64 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-'))
        {
            throw new ArgumentException("Invalid identifier.", nameof(token));
        }

        return token;
    }
}
