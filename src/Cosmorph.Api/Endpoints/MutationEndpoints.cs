using Cosmorph.Api.Authentication;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;
using Cosmorph.Infrastructure.Configuration;

namespace Cosmorph.Api.Endpoints;

/// <summary>
/// Authenticated mutation contracts. A caller signs in with Microsoft Entra ID and presents a bearer
/// token carrying the mutation scope; the actor is then the token's directory object identifier and
/// a world may only be reconfigured by the actor who created it. Where authentication is not
/// configured the whole surface fails closed. There is no development-header bypass.
/// </summary>
public static class MutationEndpoints
{
    public const int MaxIdempotencyKeyLength = 64;
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void MapMutationEndpoints(
        this IEndpointRouteBuilder builder,
        bool isProduction,
        AuthenticationOptions authentication)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(authentication);

        var authenticated = authentication.IsConfigured;

        var create = builder.MapPost("/api/worlds", async (
            CreateWorldRequest request,
            HttpContext context,
            CosmorphOptions options,
            WorldFactory factory,
            CancellationToken cancellationToken) =>
        {
            if (Gate(context, authenticated, isProduction, options, out var actorId) is { } closed)
            {
                return closed;
            }

            if (!TryIdempotencyKey(context, out _))
            {
                return Problem("An Idempotency-Key header is required.", StatusCodes.Status400BadRequest);
            }

            if (request is null
                || !WorldId.TryParse(request.WorldId, out var worldId)
                || string.IsNullOrWhiteSpace(request.Name)
                || request.Name.Length > WorldFactory.MaxNameLength)
            {
                return Problem("Invalid world request.", StatusCodes.Status400BadRequest);
            }

            var result = await factory
                .CreateAsync(worldId, request.Name, new WorldSeed(request.Seed), request.IsPublic, actorId, cancellationToken)
                .ConfigureAwait(false);

            return result.Created
                ? Results.Created($"/api/worlds/{worldId.Value}", new { worldId = worldId.Value })
                : Results.Conflict(new { worldId = worldId.Value });
        });

        var charter = builder.MapPut("/api/worlds/{worldId}/wardens/{wardenId}/charter", async (
            string worldId,
            string wardenId,
            CharterRequest request,
            HttpContext context,
            CosmorphOptions options,
            IWorldStore store,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (Gate(context, authenticated, isProduction, options, out var actorId) is { } closed)
            {
                return closed;
            }

            if (!TryIdempotencyKey(context, out var idempotencyKey))
            {
                return Problem("An Idempotency-Key header is required.", StatusCodes.Status400BadRequest);
            }

            if (!WorldId.TryParse(worldId, out var world) || !WardenId.TryParse(wardenId, out var warden))
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            if (request is null)
            {
                return Problem("Invalid charter.", StatusCodes.Status400BadRequest);
            }

            var manifest = await store.TryGetManifestAsync(world, cancellationToken).ConfigureAwait(false);

            // A world nobody owns, and a world owned by somebody else, are both reported as absent:
            // the spectator surface already refuses to disclose which worlds exist.
            if (manifest is not { } found || !IsOwnedBy(found.Manifest, actorId))
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            // The world is known before the charter is built, so the region can be bounded by this
            // world's own grid. Without that the API accepts a charter the domain later refuses, and
            // the caller gets 202 followed by nothing happening.
            var cellCount = found.Manifest.GridWidth * found.Manifest.GridHeight;
            if (!TryBuildCharter(warden, request, cellCount, out var charter))
            {
                return Problem("Invalid charter.", StatusCodes.Status400BadRequest);
            }

            // The actor is resolved server-side; scope identifiers in a body are never trusted.
            var command = new WorldCommand
            {
                CommandId = idempotencyKey,
                WorldId = world,
                Kind = WorldCommandKind.SetWardenCharter,
                ActorId = actorId,
                CreatedAtUtc = clock.UtcNow,
                Charter = charter,
            };

            var accepted = await store.TryWriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            return Results.Accepted($"/api/worlds/{world.Value}", new { accepted, commandId = command.CommandId });
        });

        var wardens = builder.MapGet("/api/worlds/{worldId}/wardens", async (
            string worldId,
            HttpContext context,
            CosmorphOptions options,
            IWorldStore store,
            CancellationToken cancellationToken) =>
        {
            if (Gate(context, authenticated, isProduction, options, out var actorId) is { } closed)
            {
                return closed;
            }

            if (!WorldId.TryParse(worldId, out var world))
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            var manifest = await store.TryGetManifestAsync(world, cancellationToken).ConfigureAwait(false);
            if (manifest is not { } found || !IsOwnedBy(found.Manifest, actorId))
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            var state = await store
                .TryLoadSnapshotAsync(found.Manifest.Id, found.Manifest.Tick, cancellationToken)
                .ConfigureAwait(false);
            if (state is null)
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            // Charters are owner-only configuration, so they are never cached by a shared proxy.
            context.Response.Headers.CacheControl = "no-store";

            var charters = state.Wardens
                .OrderBy(w => w.Id.Value, StringComparer.Ordinal)
                .Select(w => new CharterView
                {
                    WardenId = w.Id.Value,
                    DisplayName = w.DisplayName,
                    ControlledSpecies = w.ControlledSpecies.Value,
                    ControlledRegion = [.. w.ControlledRegion],
                    Goals = [.. w.Goals.Select(g => g.ToString())],
                    GoalWeights = [.. w.GoalWeights],
                    Taboos = [.. w.Taboos.Select(t => t.ToString())],
                    ActionBudget = w.ActionBudget,
                    BudgetRenewalPerChapter = w.BudgetRenewalPerChapter,
                    ImpactCeilingPerChapter = w.ImpactCeilingPerChapter,
                    ImpactUsedThisChapter = w.ImpactUsedThisChapter,
                })
                .ToArray();

            return Results.Ok(new CharterList(
                CharterList.CurrentSchema,
                world.Value,
                state.Version,
                found.Manifest.GridWidth,
                found.Manifest.GridHeight,
                charters));
        });

        if (authenticated)
        {
            create.RequireAuthorization(MutationAuthentication.PolicyName);
            charter.RequireAuthorization(MutationAuthentication.PolicyName);
            wardens.RequireAuthorization(MutationAuthentication.PolicyName);
        }
    }

    internal const string LocalActorId = "local-developer";

    /// <summary>Only the actor who created a world may reconfigure it.</summary>
    private static bool IsOwnedBy(WorldManifest manifest, string actorId) =>
        manifest.OwnerId is { Length: > 0 } owner && string.Equals(owner, actorId, StringComparison.Ordinal);

    /// <summary>
    /// Resolves the actor, or refuses the request. With authentication configured the authorization
    /// policy has already rejected anonymous and unscoped callers, so only a token missing an object
    /// identifier remains. Without it, mutations are reachable only in a local, non-production
    /// process running entirely on local doubles.
    /// </summary>
    private static IResult? Gate(
        HttpContext context,
        bool authenticated,
        bool isProduction,
        CosmorphOptions options,
        out string actorId)
    {
        ArgumentNullException.ThrowIfNull(options);
        actorId = string.Empty;

        if (authenticated)
        {
            return MutationAuthentication.TryGetActorId(context.User, out actorId)
                ? null
                : Problem("The token carries no directory object identifier.", StatusCodes.Status403Forbidden);
        }

        if (isProduction || !options.UseInMemoryStore || !options.UseFakeWorldmind)
        {
            return Problem("Mutation endpoints require production authentication, which is not configured.",
                StatusCodes.Status501NotImplemented);
        }

        actorId = LocalActorId;
        return null;
    }

    private static bool TryIdempotencyKey(HttpContext context, out string key)
    {
        key = context.Request.Headers[IdempotencyHeader].ToString();
        return key.Length is > 0 and <= MaxIdempotencyKeyLength
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static bool TryBuildCharter(WardenId wardenId, CharterRequest request, int cellCount, out WardenCharter charter)
    {
        charter = null!;
        var displayName = request.DisplayName;
        var goals = request.Goals;
        var weights = request.GoalWeights;
        var taboos = request.Taboos;
        var region = request.ControlledRegion;
        if (displayName is null || goals is null || weights is null || taboos is null || region is null)
        {
            return false;
        }

        var species = new SpeciesId(request.ControlledSpecies ?? string.Empty);
        if (displayName.Length is 0 or > WardenCharter.MaxDisplayNameLength
            || goals.Count is 0 or > WardenCharter.MaxGoals
            || weights.Count != goals.Count
            || taboos.Count > WardenCharter.MaxTaboos
            || region.Count is 0 || region.Count > WardenCharter.MaxRegionCells
            || goals.Any(g => !Enum.IsDefined(g))
            || goals.Distinct().Count() != goals.Count
            || taboos.Any(t => !Enum.IsDefined(t))
            || weights.Any(w => w is < 1 or > WardenCharter.MaxWeight)
            || region.Any(c => c < 0 || c >= cellCount)
            || request.ActionBudget is < 0 or > WardenCharter.MaxBudget
            || request.BudgetRenewalPerChapter is < 0 or > WardenCharter.MaxBudget
            || request.ImpactCeilingPerChapter is < 0 or > WardenCharter.MaxImpactCeiling
            || !ContentPack.Season1.TryGet(species, out _))
        {
            return false;
        }

        charter = new WardenCharter
        {
            Id = wardenId,
            DisplayName = displayName,
            ControlledSpecies = species,
            ControlledRegion = [.. region.Distinct().Order()],
            Goals = [.. goals],
            GoalWeights = [.. weights],
            Taboos = [.. taboos.Distinct()],
            ActionBudget = request.ActionBudget,
            BudgetRenewalPerChapter = request.BudgetRenewalPerChapter,
            ImpactCeilingPerChapter = request.ImpactCeilingPerChapter,
            ImpactUsedThisChapter = 0,
        };
        return true;
    }

    private static IResult Problem(string title, int statusCode) => Results.Problem(title: title, statusCode: statusCode);
}

public sealed record CreateWorldRequest(string WorldId, string Name, ulong Seed, bool IsPublic);

/// <summary>
/// A charter as returned to the world's owner. Owner-scoped: this shape never appears on the
/// anonymous spectator surface.
/// </summary>
public sealed record CharterView
{
    public required string WardenId { get; init; }

    public required string DisplayName { get; init; }

    public required string ControlledSpecies { get; init; }

    public required int[] ControlledRegion { get; init; }

    public required string[] Goals { get; init; }

    public required int[] GoalWeights { get; init; }

    public required string[] Taboos { get; init; }

    public required int ActionBudget { get; init; }

    public required int BudgetRenewalPerChapter { get; init; }

    public required int ImpactCeilingPerChapter { get; init; }

    public required int ImpactUsedThisChapter { get; init; }
}

public sealed record CharterList(
    string Schema,
    string WorldId,
    long Version,
    int GridWidth,
    int GridHeight,
    IReadOnlyList<CharterView> Wardens)
{
    public const string CurrentSchema = "warden-charters/1";
}

public sealed record CharterRequest
{
    public required string DisplayName { get; init; }

    public required string ControlledSpecies { get; init; }

    public required IReadOnlyList<int> ControlledRegion { get; init; }

    public required IReadOnlyList<WardenGoal> Goals { get; init; }

    public required IReadOnlyList<int> GoalWeights { get; init; }

    public required IReadOnlyList<WardenTaboo> Taboos { get; init; }

    public required int ActionBudget { get; init; }

    public required int BudgetRenewalPerChapter { get; init; }

    public required int ImpactCeilingPerChapter { get; init; }
}
