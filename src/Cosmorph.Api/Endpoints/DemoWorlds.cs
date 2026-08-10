using Cosmorph.Application.Worlds;
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
        foreach (var (id, name, seed) in Definitions)
        {
            await factory
                .CreateAsync(WorldId.Parse(id), name, new WorldSeed(seed), isPublic: true, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
