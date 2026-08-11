using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worlds;
using Cosmorph.Domain.Content;
using Cosmorph.Domain.Ecology;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;
using Cosmorph.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cosmorph.Api.Endpoints;

/// <summary>
/// Seeds two demonstration worlds in local mode so the Observatory has something to watch.
/// Seeding creates worlds only; it never advances them, because the TickJob is the only writer of
/// simulation outcomes.
/// </summary>
public static class DemoWorlds
{
    public static readonly (string Id, string Name, ulong Seed)[] Definitions =
    [
        ("verdant-cradle", "Verdant Cradle", 12345UL),
        ("ashen-tide", "Ashen Tide", 987654321UL),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.GetRequiredService<CosmorphOptions>();
        if (!options.SeedDemoWorlds)
        {
            return;
        }

        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<WorldFactory>();
        var store = scope.ServiceProvider.GetRequiredService<IWorldStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        foreach (var (id, name, seed) in Definitions)
        {
            var worldId = WorldId.Parse(id);
            var result = await factory
                .CreateAsync(
                    worldId,
                    name,
                    new WorldSeed(seed),
                    isPublic: true,

                    // Seeding only runs in local mode, so the local actor owns the demo worlds and can
                    // reconfigure their Wardens through the ordinary authenticated-shaped path.
                    MutationEndpoints.LocalActorId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.Created)
            {
                continue;
            }

            foreach (var command in DemoCharterCommands(worldId, new WorldSeed(seed), clock.UtcNow))
            {
                await store.TryWriteCommandAsync(command, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Demo Wardens are created through the ordinary validated command path, so the demo exercises the
    /// same containment rules as a user-configured charter.
    /// </summary>
    public static IReadOnlyList<WorldCommand> DemoCharterCommands(WorldId worldId, WorldSeed seed, DateTimeOffset now)
    {
        var state = WorldGenerator.Create(worldId, "demo", seed, WorldFactory.DefaultWidth, WorldFactory.DefaultHeight);
        var commands = new List<WorldCommand>();
        var wardens = new (string Id, string Name, string Species, WardenGoal[] Goals, int[] Weights, WardenTaboo[] Taboos)[]
        {
            ("moss-keeper", "Moss Keeper", "verdant-moss", [WardenGoal.Preserve, WardenGoal.Expand], [70, 30], [WardenTaboo.NeverBurn]),
            ("herd-shepherd", "Herd Shepherd", "cliff-grazer", [WardenGoal.Adapt, WardenGoal.Cooperate], [60, 40], [WardenTaboo.NeverHunt]),
            ("stalker-warden", "Stalker Warden", "ember-stalker", [WardenGoal.Hunt, WardenGoal.Preserve], [55, 45], []),
        };

        foreach (var warden in wardens)
        {
            var species = new SpeciesId(warden.Species);
            var region = state.Populations
                .Where(p => p.Species == species && p.Population > 0)
                .OrderByDescending(p => p.Population)
                .ThenBy(p => p.CellIndex)
                .Select(p => p.CellIndex)
                .Distinct()
                .Take(64)
                .ToArray();
            if (region.Length == 0)
            {
                continue;
            }

            var charter = new WardenCharter
            {
                Id = WardenId.Parse(warden.Id),
                DisplayName = warden.Name,
                ControlledSpecies = species,
                ControlledRegion = region,
                Goals = warden.Goals,
                GoalWeights = warden.Weights,
                Taboos = warden.Taboos,
                ActionBudget = 40,
                BudgetRenewalPerChapter = 20,
                ImpactCeilingPerChapter = 60,
                ImpactUsedThisChapter = 0,
            };

            commands.Add(new WorldCommand
            {
                CommandId = $"demo-charter-{worldId.Value}-{warden.Id}",
                WorldId = worldId,
                Kind = WorldCommandKind.SetWardenCharter,
                ActorId = "system:demo-seed",
                CreatedAtUtc = now,
                Charter = charter,
            });
        }

        return commands;
    }
}
