using System.Security.Claims;
using Cosmorph.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Cosmorph.Api.Authentication;

/// <summary>
/// Microsoft Entra ID bearer authentication for the mutation surface. Tokens are validated against
/// the directory's published signing keys, so the API holds no secret of any kind. Spectator reads
/// stay anonymous; only mutations require a token.
/// </summary>
public static class MutationAuthentication
{
    /// <summary>Authorization policy every mutation endpoint requires.</summary>
    public const string PolicyName = "mutate";

    /// <summary>Distinguishes a directory principal from the local development actor.</summary>
    public const string ActorPrefix = "entra:";

    private const string ObjectIdClaim = "oid";
    private const string LegacyObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ScopeClaim = "scp";

    /// <summary>
    /// Registers bearer validation and the mutation policy. Does nothing when authentication is not
    /// configured, which keeps the local development loop unauthenticated and leaves Production
    /// failing closed on <see cref="AuthenticationOptions.ValidateForEnvironment"/>.
    /// </summary>
    public static IServiceCollection AddMutationAuthentication(
        this IServiceCollection services,
        AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            return services;
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.RequireHttpsMetadata = true;

                // Short claim names are what the directory issues. Mapping them to long WS-Fed URIs
                // would only make the actor and scope lookups below harder to read.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,

                    // A token minted for this application carries either the application identifier
                    // or the application ID URI, depending on how the client asked for it.
                    ValidAudiences = [options.ClientId!, $"api://{options.ClientId}"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
            });

        services.AddAuthorization(authorization => authorization.AddPolicy(
            PolicyName,
            policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasScope(context.User, options.Scope))));

        return services;
    }

    /// <summary>True when the caller holds the delegated scope that permits world mutations.</summary>
    public static bool HasScope(ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);

        // The scope claim is a single space-delimited string, not one claim per scope.
        foreach (var claim in user.FindAll(ScopeClaim))
        {
            foreach (var granted in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the durable actor identifier from the token. The object identifier is used because it
    /// is stable across renames and sign-in names, and it is required to be a directory GUID so that
    /// nothing caller-controlled reaches a stored command.
    /// </summary>
    public static bool TryGetActorId(ClaimsPrincipal user, out string actorId)
    {
        ArgumentNullException.ThrowIfNull(user);
        actorId = string.Empty;

        var objectId = user.FindFirst(ObjectIdClaim)?.Value ?? user.FindFirst(LegacyObjectIdClaim)?.Value;
        if (objectId is null || !Guid.TryParse(objectId, out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        actorId = ActorPrefix + parsed.ToString("D");
        return true;
    }
}
