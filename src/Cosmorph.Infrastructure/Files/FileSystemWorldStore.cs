using System.Globalization;
using System.Text;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Scheduling;
using Cosmorph.Application.Serialization;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Infrastructure.Files;

/// <summary>
/// Local-development store backed by a directory. It mirrors the Blob layout, concurrency token and
/// lease semantics so the API and the TickJob can run as separate processes without Azure.
/// Never used in Production.
/// </summary>
public sealed class FileSystemWorldStore : IWorldStore, IWorldSchedule
{
    private readonly string _root;

    public FileSystemWorldStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<ManifestWithToken?> TryGetManifestAsync(WorldId worldId, CancellationToken cancellationToken)
    {
        var path = Resolve($"worlds/{Validate(worldId)}/manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return new ManifestWithToken(CanonicalJson.Deserialize<WorldManifest>(json), Token(path));
    }

    public async Task<IReadOnlyList<WorldManifest>> ListPublicWorldsAsync(CancellationToken cancellationToken)
    {
        var worldsRoot = Resolve("worlds");
        if (!Directory.Exists(worldsRoot))
        {
            return [];
        }

        var manifests = new List<WorldManifest>();
        foreach (var path in Directory.EnumerateFiles(worldsRoot, "manifest.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var manifest = CanonicalJson.Deserialize<WorldManifest>(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
            if (manifest.IsPublic)
            {
                manifests.Add(manifest);
            }
        }

        return [.. manifests.OrderBy(m => m.Id.Value, StringComparer.Ordinal)];
    }

    public async Task<WorldState?> TryLoadSnapshotAsync(WorldId worldId, long tick, CancellationToken cancellationToken)
    {
        var path = SnapshotPath(worldId, tick);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return SnapshotCodec.ToState(CanonicalJson.Deserialize<SnapshotDocument>(json));
    }

    public Task WriteSnapshotAsync(WorldState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WriteAsync(
            SnapshotPath(state.Id, state.Tick.Value),
            CanonicalJson.Serialize(SnapshotCodec.ToDocument(state)),
            cancellationToken);
    }

    public async Task AppendEventsAsync(WorldId worldId, IReadOnlyList<WorldEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        var path = Resolve($"worlds/{Validate(worldId)}/events/chronicle.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var known = new HashSet<long>();
        if (File.Exists(path))
        {
            foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
            {
                if (line.Length > 0)
                {
                    known.Add(CanonicalJson.Deserialize<EventDocument>(line).Sequence);
                }
            }
        }

        var builder = new StringBuilder();
        foreach (var worldEvent in events.OrderBy(e => e.Sequence))
        {
            if (!known.Add(worldEvent.Sequence))
            {
                continue;
            }

            builder.Append(CanonicalJson.Serialize(EventCodec.ToDocument(worldEvent))).Append('\n');
        }

        if (builder.Length > 0)
        {
            await File.AppendAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<WorldEvent>> ReadEventsAsync(WorldId worldId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var path = Resolve($"worlds/{Validate(worldId)}/events/chronicle.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return
        [
            .. lines
                .Where(l => l.Length > 0)
                .Select(l => EventCodec.ToEvent(CanonicalJson.Deserialize<EventDocument>(l)))
                .Where(e => e.Sequence > afterSequence)
                .OrderBy(e => e.Sequence)
                .Take(limit)
        ];
    }

    public async Task<bool> TryCreateAsync(WorldState state, WorldManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(manifest);
        var path = Resolve($"worlds/{Validate(manifest.Id)}/manifest.json");
        if (File.Exists(path))
        {
            return false;
        }

        await WriteSnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        await WriteAsync(path, CanonicalJson.Serialize(manifest), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TryReplaceManifestAsync(WorldManifest manifest, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var path = Resolve($"worlds/{Validate(manifest.Id)}/manifest.json");
        if (!File.Exists(path) || !string.Equals(Token(path), concurrencyToken, StringComparison.Ordinal))
        {
            return false;
        }

        await WriteAsync(path, CanonicalJson.Serialize(manifest), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<IWorldLease?> TryAcquireLeaseAsync(WorldId worldId, TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve($"worlds/{Validate(worldId)}/lease.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return Task.FromResult<IWorldLease?>(new FileLease(worldId, stream));
        }
        catch (IOException)
        {
            return Task.FromResult<IWorldLease?>(null);
        }
    }

    public Task WriteDecisionAuditAsync(DecisionAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return WriteAsync(
            Resolve($"audit/worldmind/{Validate(record.WorldId)}/{Hash(record.Fingerprint)}.json"),
            CanonicalJson.Serialize(record),
            cancellationToken);
    }

    public async Task<DecisionAuditRecord?> TryGetDecisionAsync(WorldId worldId, string fingerprint, CancellationToken cancellationToken)
    {
        var path = Resolve($"audit/worldmind/{Validate(worldId)}/{Hash(fingerprint)}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var record = CanonicalJson.Deserialize<DecisionAuditRecord>(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
        return record.WorldId == worldId ? record : null;
    }

    public async Task<bool> TryWriteCommandAsync(WorldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var path = Resolve($"worlds/{Validate(command.WorldId)}/commands/{Hash(command.CommandId)}.json");
        if (File.Exists(path))
        {
            return false;
        }

        await WriteAsync(path, CanonicalJson.Serialize(command), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<WorldCommand>> ReadPendingCommandsAsync(WorldId worldId, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var directory = Resolve($"worlds/{Validate(worldId)}/commands");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var commands = new List<WorldCommand>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var command = CanonicalJson.Deserialize<WorldCommand>(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
            if (command.WorldId != worldId)
            {
                continue;
            }

            if (!File.Exists(Resolve($"worlds/{Validate(worldId)}/commands-applied/{Hash(command.CommandId)}.json")))
            {
                commands.Add(command);
            }
        }

        return [.. commands.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.CommandId, StringComparer.Ordinal).Take(limit)];
    }

    public async Task MarkCommandsAppliedAsync(WorldId worldId, IReadOnlyList<string> commandIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        foreach (var commandId in commandIds)
        {
            await WriteAsync(
                Resolve($"worlds/{Validate(worldId)}/commands-applied/{Hash(commandId)}.json"),
                "{}",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task MarkDueAsync(WorldId worldId, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        var bucket = TickRunner.Bucket(instant).UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        return WriteAsync(Resolve($"schedule/{bucket}/00/{Validate(worldId)}.json"), "{}", cancellationToken);
    }

    public Task<IReadOnlyList<WorldId>> ReadBucketAsync(DateTimeOffset bucket, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var key = TickRunner.Bucket(bucket).UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var directory = Resolve($"schedule/{key}/00");
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<WorldId>>([]);
        }

        IReadOnlyList<WorldId> worlds =
        [
            .. Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null && WorldId.TryParse(name, out _))
                .Select(name => WorldId.Parse(name!))
                .OrderBy(w => w.Value, StringComparer.Ordinal)
                .Take(limit)
        ];

        return Task.FromResult(worlds);
    }

    public async Task<DateTimeOffset?> TryGetWatermarkAsync(CancellationToken cancellationToken)
    {
        var path = Resolve("scheduler/watermark/00.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.Parse(text.Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public Task SetWatermarkAsync(DateTimeOffset watermark, CancellationToken cancellationToken) =>
        WriteAsync(
            Resolve("scheduler/watermark/00.json"),
            $"\"{watermark.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}\"",
            cancellationToken);

    private static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Validate(WorldId worldId) =>
        WorldId.TryParse(worldId.Value, out var validated)
            ? validated.Value
            : throw new ArgumentException("Invalid world identifier.", nameof(worldId));

    private static string Token(string path) =>
        File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) + ":" + new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture);

    private string SnapshotPath(WorldId worldId, long tick) =>
        Resolve($"worlds/{Validate(worldId)}/snapshots/{tick.ToString("D20", CultureInfo.InvariantCulture)}.json");

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved path escaped the store root.");
        }

        return full;
    }

    private static Task<string> ReadAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed class FileLease(WorldId worldId, FileStream stream) : IWorldLease
    {
        public WorldId WorldId { get; } = worldId;

        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
