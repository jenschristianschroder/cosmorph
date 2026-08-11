using System.Globalization;

namespace Cosmorph.Infrastructure.Configuration;

/// <summary>
/// Microsoft Entra ID settings for authenticated mutations. A directory identifier, an application
/// identifier and a scope name are not secrets; no client secret or certificate exists, because the
/// browser signs in with authorization code and PKCE and the API validates tokens against the
/// published signing keys.
/// </summary>
public sealed record AuthenticationOptions
{
    /// <summary>Entra directory (tenant) identifier. Empty leaves mutations closed.</summary>
    public string? TenantId { get; init; }

    /// <summary>Application (client) identifier. It is both the browser client and the token audience.</summary>
    public string? ClientId { get; init; }

    /// <summary>Delegated scope a caller must hold to mutate a world.</summary>
    public string Scope { get; init; } = "World.Write";

    /// <summary>
    /// True once a directory and an application are configured. Everything else in the API keys off
    /// this: without it there is no way to authenticate, so mutations must stay closed.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(Scope);

    /// <summary>Authority for token validation and for the browser's sign-in request.</summary>
    public string Authority =>
        string.Create(CultureInfo.InvariantCulture, $"https://login.microsoftonline.com/{TenantId}/v2.0");

    /// <summary>Issuer the API pins. Only this directory may issue an accepted token.</summary>
    public string Issuer => Authority;

    /// <summary>Fully qualified scope the browser requests, for example api://guid/World.Write.</summary>
    public string ScopeUri => string.Create(CultureInfo.InvariantCulture, $"api://{ClientId}/{Scope}");

    /// <summary>
    /// Fails closed in Production. Starting a production API that cannot authenticate anyone would
    /// serve mutations as a permanent 501, which reads like a deployment fault rather than a
    /// deliberate posture.
    /// </summary>
    public void ValidateForEnvironment(bool isProduction)
    {
        if (!isProduction || IsConfigured)
        {
            return;
        }

        throw new InvalidOperationException(
            "Production requires Entra authentication. Set Cosmorph:Authentication:TenantId and ClientId.");
    }
}
