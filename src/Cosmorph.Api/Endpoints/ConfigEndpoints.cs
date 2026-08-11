using Cosmorph.Infrastructure.Configuration;

namespace Cosmorph.Api.Endpoints;

/// <summary>
/// Anonymous, non-secret client configuration. The Observatory is a static build served by this
/// container, so it cannot be compiled with a directory identifier; it reads one here at runtime.
/// A tenant identifier, an application identifier and a scope name are public by design: they are
/// what any browser sending a sign-in request already discloses.
/// </summary>
public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/config", (AuthenticationOptions authentication) => Results.Ok(
            new ClientConfig(authentication.IsConfigured
                ? new ClientAuthConfig(authentication.TenantId!, authentication.ClientId!, authentication.ScopeUri)
                : null)));
    }
}

/// <summary>Client configuration. A null authentication block means sign-in is not available.</summary>
public sealed record ClientConfig(ClientAuthConfig? Auth);

public sealed record ClientAuthConfig(string TenantId, string ClientId, string Scope);
