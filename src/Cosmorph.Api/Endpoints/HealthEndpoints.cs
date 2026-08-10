using Cosmorph.Application.Abstractions;

namespace Cosmorph.Api.Endpoints;

/// <summary>Liveness and readiness probes. Neither reveals configuration or credentials.</summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        builder.MapGet("/health/ready", async (IWorldStore store, CancellationToken cancellationToken) =>
        {
            try
            {
                await store.ListPublicWorldsAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Problem(title: "Not ready", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}
