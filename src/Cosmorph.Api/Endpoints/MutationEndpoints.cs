using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;
using Cosmorph.Infrastructure.Configuration;

namespace Cosmorph.Api.Endpoints;

/// <summary>
/// Authenticated mutation contracts. Production authentication is not configured during this
/// milestone, so production mutations fail closed. There is no development-header bypass.
/// </summary>
public static class MutationEndpoints
{
    public const int MaxIdempotencyKeyLength = 64;
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void MapMutationEndpoints(this IEndpointRouteBuilder builder, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapPost("/api/worlds", async (
            CreateWorldRequest request,
            HttpContext context,
            CosmorphOptions options,
            WorldFactory factory,
            CancellationToken cancellationToken) =>
        {
            if (Closed(isProduction, options) is { } closed)
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
                .CreateAsync(worldId, request.Name, new WorldSeed(request.Seed), request.IsPublic, cancellationToken)
                .ConfigureAwait(false);

            return result.Created
                ? Results.Created($"/api/worlds/{worldId.Value}", new { worldId = worldId.Value })
                : Results.Conflict(new { worldId = worldId.Value });
        });

        builder.MapPut("/api/worlds/{worldId}/wardens/{wardenId}/charter", async (
            string worldId,
            string wardenId,
            CharterRequest request,
            HttpContext context,
            CosmorphOptions options,
            IWorldStore store,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (Closed(isProduction, options) is { } closed)
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

            if (request is null || !TryBuildCharter(warden, request, out var charter))
            {
                return Problem("Invalid charter.", StatusCodes.Status400BadRequest);
            }

            var manifest = await store.TryGetManifestAsync(world, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                return Problem("World not found.", StatusCodes.Status404NotFound);
            }

            // The actor is resolved server-side; scope identifiers in a body are never trusted.
            var command = new WorldCommand
            {
                CommandId = idempotencyKey,
                WorldId = world,
                Kind = WorldCommandKind.SetWardenCharter,
                ActorId = LocalActorId,
                CreatedAtUtc = clock.UtcNow,
                Charter = charter,
            };

            var accepted = await store.TryWriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            return Results.Accepted($"/api/worlds/{world.Value}", new { accepted, commandId = command.CommandId });
        });
    }

    internal const string LocalActorId = "local-developer";

    private static IResult? Closed(bool isProduction, CosmorphOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Fail closed: mutations are only reachable in a local, non-production process.
        return isProduction || !options.UseInMemoryStore || !options.UseFakeWorldmind
            ? Problem("Mutation endpoints require production authentication, which is not configured.",
                StatusCodes.Status501NotImplemented)
            : null;
    }

    private static bool TryIdempotencyKey(HttpContext context, out string key)
    {
        key = context.Request.Headers[IdempotencyHeader].ToString();
        return key.Length is > 0 and <= MaxIdempotencyKeyLength
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static bool TryBuildCharter(WardenId wardenId, CharterRequest request, out WardenCharter charter)
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
            || region.Count > 4096
            || goals.Any(g => !Enum.IsDefined(g))
            || taboos.Any(t => !Enum.IsDefined(t))
            || weights.Any(w => w is < 1 or > WardenCharter.MaxWeight)
            || region.Any(c => c < 0)
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
