using System.Globalization;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Spectator;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Api.Endpoints;

/// <summary>
/// Anonymous, read-only spectator surface. Only public worlds are reachable and every response is a
/// presentation DTO; persistence and domain objects never leave the process.
/// </summary>
public static class SpectatorEndpoints
{
    public const int MaxEventLimit = 200;
    private const int DefaultEventLimit = 50;
    private const int DefaultNeighbourhoodRadius = 1;

    public static void MapSpectatorEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/worlds", async (IWorldStore store, IGameMaster gameMaster, CancellationToken cancellationToken) =>
        {
            var manifests = await store.ListPublicWorldsAsync(cancellationToken).ConfigureAwait(false);
            var worlds = manifests
                .Select(m => new PublicWorldListItem(m.Id.Value, m.Name, m.Tick, m.Chapter, m.LastAdvancedAtUtc))
                .ToArray();
            return Results.Ok(new PublicWorldList(worlds, gameMaster.Name));
        });

        builder.MapGet("/api/worlds/{worldId}", async (
            string worldId,
            HttpContext context,
            IWorldStore store,
            IGameMaster gameMaster,
            CancellationToken cancellationToken) =>
        {
            var loaded = await LoadAsync(store, worldId, cancellationToken).ConfigureAwait(false);
            if (loaded is not { } summary)
            {
                return NotFound();
            }

            var (manifest, state) = summary;
            var etag = Etag(manifest.Id, manifest.Version, "summary");
            if (IsNotModified(context, etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            SetCacheHeaders(context, etag);
            return Results.Ok(SpectatorMapper.ToSummary(state, manifest.LastAdvancedAtUtc, gameMaster.Name));
        });

        builder.MapGet("/api/worlds/{worldId}/snapshot", async (
            string worldId,
            HttpContext context,
            IWorldStore store,
            CancellationToken cancellationToken) =>
        {
            var loaded = await LoadAsync(store, worldId, cancellationToken).ConfigureAwait(false);
            if (loaded is not { } snapshot)
            {
                return NotFound();
            }

            var (manifest, state) = snapshot;
            var etag = Etag(manifest.Id, manifest.Version, "snapshot");
            if (IsNotModified(context, etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            SetCacheHeaders(context, etag);
            return Results.Ok(SpectatorMapper.ToSnapshot(state));
        });

        builder.MapGet("/api/worlds/{worldId}/cells/{cellIndex:int}", async (
            string worldId,
            int cellIndex,
            HttpContext context,
            IWorldStore store,
            CancellationToken cancellationToken) =>
        {
            var loaded = await LoadAsync(store, worldId, cancellationToken).ConfigureAwait(false);
            if (loaded is not { } place)
            {
                return NotFound();
            }

            var (manifest, state) = place;
            var detail = SpectatorMapper.ToCellDetail(state, cellIndex);
            if (detail is null)
            {
                // An index outside the grid is answered exactly like an unknown world.
                return NotFound();
            }

            var etag = Etag(manifest.Id, manifest.Version, string.Create(CultureInfo.InvariantCulture, $"cell{cellIndex}"));
            if (IsNotModified(context, etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            SetCacheHeaders(context, etag);
            return Results.Ok(detail);
        });

        builder.MapGet("/api/worlds/{worldId}/cells/{cellIndex:int}/neighbourhood", async (
            string worldId,
            int cellIndex,
            int? radius,
            HttpContext context,
            IWorldStore store,
            CancellationToken cancellationToken) =>
        {
            // Validate the radius before loading anything, as the events endpoint does with its cursor.
            var reach = radius ?? DefaultNeighbourhoodRadius;
            if (reach < 0 || reach > NeighbourhoodDto.MaxRadius)
            {
                return Results.Problem(title: "Invalid radius.", statusCode: StatusCodes.Status400BadRequest);
            }

            var loaded = await LoadAsync(store, worldId, cancellationToken).ConfigureAwait(false);
            if (loaded is not { } around)
            {
                return NotFound();
            }

            var (manifest, state) = around;
            var block = SpectatorMapper.ToNeighbourhood(state, cellIndex, reach);
            if (block is null)
            {
                // A centre outside the grid is answered exactly like an unknown world.
                return NotFound();
            }

            var etag = Etag(manifest.Id, manifest.Version, string.Create(CultureInfo.InvariantCulture, $"cells{cellIndex}r{reach}"));
            if (IsNotModified(context, etag))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            SetCacheHeaders(context, etag);
            return Results.Ok(block);
        });

        builder.MapGet("/api/worlds/{worldId}/events", async (
            string worldId,
            long? after,
            int? limit,
            IWorldStore store,
            CancellationToken cancellationToken) =>
        {
            // Validate limits before allocating anything.
            var cursor = after ?? 0;
            if (cursor < 0)
            {
                return Results.Problem(title: "Invalid cursor.", statusCode: StatusCodes.Status400BadRequest);
            }

            var pageSize = limit ?? DefaultEventLimit;
            if (pageSize is < 1 or > MaxEventLimit)
            {
                return Results.Problem(title: "Invalid limit.", statusCode: StatusCodes.Status400BadRequest);
            }

            var manifest = await TryGetPublicManifestAsync(store, worldId, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                return NotFound();
            }

            var events = await store.ReadEventsAsync(manifest.Id, cursor, pageSize, cancellationToken).ConfigureAwait(false);
            var projected = events
                .Select(e => SpectatorMapper.ToEvent(e, manifest.GridWidth, manifest.GridHeight))
                .ToArray();

            return Results.Ok(new SpectatorEventPage(
                projected,
                projected.Length == 0 ? cursor : projected[^1].Sequence,
                manifest.LastEventSequence));
        });
    }

    private static async Task<Application.Worlds.WorldManifest?> TryGetPublicManifestAsync(
        IWorldStore store,
        string worldId,
        CancellationToken cancellationToken)
    {
        if (!WorldId.TryParse(worldId, out var id))
        {
            return null;
        }

        var manifest = await store.TryGetManifestAsync(id, cancellationToken).ConfigureAwait(false);
        return manifest is { Manifest.IsPublic: true } found ? found.Manifest : null;
    }

    private static async Task<(Application.Worlds.WorldManifest Manifest, WorldState State)?> LoadAsync(
        IWorldStore store,
        string worldId,
        CancellationToken cancellationToken)
    {
        var manifest = await TryGetPublicManifestAsync(store, worldId, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        var state = await store.TryLoadSnapshotAsync(manifest.Id, manifest.Tick, cancellationToken).ConfigureAwait(false);
        return state is null ? null : (manifest, state);
    }

    /// <summary>Constant-shape not-found response, so world existence is never disclosed.</summary>
    private static IResult NotFound() =>
        Results.Problem(title: "World not found.", statusCode: StatusCodes.Status404NotFound);

    private static string Etag(WorldId worldId, long version, string kind) =>
        "\"" + string.Create(CultureInfo.InvariantCulture, $"{kind}-{worldId.Value}-{version}") + "\"";

    private static bool IsNotModified(HttpContext context, string etag) =>
        context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal));

    private static void SetCacheHeaders(HttpContext context, string etag)
    {
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "public, max-age=5";
    }
}

public sealed record PublicWorldListItem(string WorldId, string Name, long Tick, int Chapter, DateTimeOffset LastAdvancedAtUtc);

public sealed record PublicWorldList(IReadOnlyList<PublicWorldListItem> Worlds, string WorldmindMode);

public sealed record SpectatorEventPage(IReadOnlyList<SpectatorEventDto> Events, long Cursor, long LatestSequence);
