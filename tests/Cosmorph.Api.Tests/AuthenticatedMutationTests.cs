using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cosmorph.Application.Abstractions;
using Cosmorph.Domain.Worlds;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cosmorph.Api.Tests;

/// <summary>
/// Mutations with authentication configured: a caller must be signed in, must hold the mutation
/// scope, and may only reconfigure a world it created. The directory is never contacted — the bearer
/// handler is replaced by a stub scheme so the suite stays offline.
/// </summary>
public sealed class AuthenticatedMutationTests : IClassFixture<AuthenticatedMutationTests.AuthenticatedApiFactory>
{
    private const string Owner = "11111111-1111-1111-1111-111111111111";
    private const string Stranger = "22222222-2222-2222-2222-222222222222";
    private const string Scope = "World.Write";

    private const string ValidCharter =
        """
        {"displayName":"Moss Keeper","controlledSpecies":"verdant-moss","controlledRegion":[0,1,2],
         "goals":["Preserve"],"goalWeights":[100],"taboos":["NeverBurn"],
         "actionBudget":10,"budgetRenewalPerChapter":5,"impactCeilingPerChapter":20}
        """;

    private readonly AuthenticatedApiFactory _factory;

    public AuthenticatedMutationTests(AuthenticatedApiFactory factory) => _factory = factory;

    /// <summary>
    /// Hosts the API with authentication configured. The directory identifiers are arbitrary: nothing
    /// in the test reaches the directory, because the stub scheme below is the default one.
    /// </summary>
    public sealed class AuthenticatedApiFactory : WebApplicationFactory<Program>
    {
        static AuthenticatedApiFactory()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("Cosmorph__UseInMemoryStore", "true");
            Environment.SetEnvironmentVariable("Cosmorph__UseFakeWorldmind", "true");
            Environment.SetEnvironmentVariable("Cosmorph__SeedDemoWorlds", "false");
        }

        public const string TenantId = "00000000-0000-0000-0000-0000000000aa";
        public const string ClientId = "00000000-0000-0000-0000-0000000000bb";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.UseEnvironment("Testing");

            // Settings rather than environment variables: authentication must be on for this host only,
            // so the unauthenticated local-development tests keep exercising the other path.
            builder.UseSetting("Cosmorph:Authentication:TenantId", TenantId);
            builder.UseSetting("Cosmorph:Authentication:ClientId", ClientId);

            builder.ConfigureTestServices(services => services
                .AddAuthentication(StubScheme.Name)
                .AddScheme<AuthenticationSchemeOptions, StubScheme>(StubScheme.Name, _ => { }));
        }
    }

    /// <summary>
    /// Stands in for the bearer handler. It mints exactly the claims a directory token would carry, so
    /// the authorization policy and actor resolution under test are the real ones.
    /// </summary>
    private sealed class StubScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Stub";
        public const string ObjectIdHeader = "X-Test-Oid";
        public const string ScopeHeader = "X-Test-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var objectId = Request.Headers[ObjectIdHeader].ToString();
            var scope = Request.Headers[ScopeHeader].ToString();
            if (objectId.Length == 0 && scope.Length == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();
            if (objectId.Length > 0)
            {
                claims.Add(new Claim("oid", objectId));
            }

            if (scope.Length > 0)
            {
                claims.Add(new Claim("scp", scope));
            }

            var identity = new ClaimsIdentity(claims, Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string uri,
        string? body,
        string idempotencyKey,
        string? objectId,
        string? scope)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = CosmorphApiFactory.JsonBody(body);
        }

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (objectId is not null)
        {
            request.Headers.TryAddWithoutValidation(StubScheme.ObjectIdHeader, objectId);
        }

        if (scope is not null)
        {
            request.Headers.TryAddWithoutValidation(StubScheme.ScopeHeader, scope);
        }

        return request;
    }

    private async Task<HttpClient> CreateWorldAsync(string worldId, string objectId)
    {
        var client = _factory.CreateClient();
        var body = $$"""{"worldId":"{{worldId}}","name":"Owned World","seed":7,"isPublic":true}""";

        using var response = await client.SendAsync(
            Request(HttpMethod.Post, "/api/worlds", body, "create-" + worldId, objectId, Scope));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task AnAnonymousMutationIsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(
            HttpMethod.Post,
            "/api/worlds",
            """{"worldId":"anon-world","name":"n","seed":1,"isPublic":true}""",
            "anon-1",
            objectId: null,
            scope: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASignedInCallerWithoutTheMutationScopeIsForbidden()
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(
            HttpMethod.Post,
            "/api/worlds",
            """{"worldId":"scopeless-world","name":"n","seed":1,"isPublic":true}""",
            "scopeless-1",
            Owner,
            scope: "User.Read"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AScopeClaimIsMatchedWholeWithinTheSpaceDelimitedList()
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(
            HttpMethod.Post,
            "/api/worlds",
            """{"worldId":"multi-scope-world","name":"n","seed":1,"isPublic":true}""",
            "multi-scope-1",
            Owner,
            scope: "User.Read World.Write offline_access"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ATokenWithoutADirectoryObjectIdentifierCannotMutate()
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(
            HttpMethod.Post,
            "/api/worlds",
            """{"worldId":"oidless-world","name":"n","seed":1,"isPublic":true}""",
            "oidless-1",
            objectId: null,
            scope: Scope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheOwnerMaySetACharterAndTheStoredActorIsTheDirectoryObjectIdentifier()
    {
        var client = await CreateWorldAsync("owned-world", Owner);

        using var response = await client.SendAsync(Request(
            HttpMethod.Put,
            "/api/worlds/owned-world/wardens/moss-keeper/charter",
            ValidCharter,
            "owned-charter-1",
            Owner,
            Scope));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // The actor is the token's object identifier, never anything the request body could set.
        var store = _factory.Services.GetRequiredService<IWorldStore>();
        var commands = await store.ReadPendingCommandsAsync(WorldId.Parse("owned-world"), 10, CancellationToken.None);
        Assert.Equal("entra:" + Owner, Assert.Single(commands).ActorId);
    }

    [Fact]
    public async Task AWorldCannotBeReconfiguredBySomebodyWhoDidNotCreateIt()
    {
        var client = await CreateWorldAsync("stranger-world", Owner);

        using var response = await client.SendAsync(Request(
            HttpMethod.Put,
            "/api/worlds/stranger-world/wardens/moss-keeper/charter",
            ValidCharter,
            "stranger-charter-1",
            Stranger,
            Scope));

        // 404 rather than 403: whether a world exists is never disclosed to a caller who cannot use it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var store = _factory.Services.GetRequiredService<IWorldStore>();
        var commands = await store.ReadPendingCommandsAsync(WorldId.Parse("stranger-world"), 10, CancellationToken.None);
        Assert.Empty(commands);
    }

    [Fact]
    public async Task AnOwnerCanReadTheChartersOfItsOwnWorld()
    {
        var client = await CreateWorldAsync("charter-read-world", Owner);

        using var response = await client.SendAsync(Request(
            HttpMethod.Get, "/api/worlds/charter-read-world/wardens", null, "read-1", Owner, Scope));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Owner-only configuration is never held by a shared cache.
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("warden-charters/1", body.GetProperty("schema").GetString());
        Assert.Equal("charter-read-world", body.GetProperty("worldId").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("wardens").ValueKind);

        // The grid is on the response so the panel can bound a region without a second read.
        Assert.True(body.GetProperty("gridWidth").GetInt32() > 0);
        Assert.True(body.GetProperty("gridHeight").GetInt32() > 0);
    }

    [Fact]
    public async Task ChartersAreNotReadableBySomebodyWhoDidNotCreateTheWorld()
    {
        var client = await CreateWorldAsync("charter-private-world", Owner);

        using var response = await client.SendAsync(Request(
            HttpMethod.Get, "/api/worlds/charter-private-world/wardens", null, "read-2", Stranger, Scope));

        // 404 rather than 403, for the same reason a charter write gives one.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChartersAreNotReadableAnonymously()
    {
        await CreateWorldAsync("charter-anon-world", Owner);
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/worlds/charter-anon-world/wardens");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ACharterOverAnImpossibleRegionIsRefusedRatherThanAcceptedAndDropped()
    {
        var client = await CreateWorldAsync("region-bound-world", Owner);

        // One past the 512 cells a charter may hold, though every index is inside the 64 by 32 grid.
        var region = string.Join(',', Enumerable.Range(0, 513));
        var body = $$"""
            {"displayName":"Too Wide","controlledSpecies":"verdant-moss","controlledRegion":[{{region}}],
             "goals":["Preserve"],"goalWeights":[100],"taboos":[],
             "actionBudget":10,"budgetRenewalPerChapter":5,"impactCeilingPerChapter":20}
            """;

        using var response = await client.SendAsync(Request(
            HttpMethod.Put,
            "/api/worlds/region-bound-world/wardens/wide-warden/charter",
            body,
            "region-bound-1",
            Owner,
            Scope));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var store = _factory.Services.GetRequiredService<IWorldStore>();
        var commands = await store.ReadPendingCommandsAsync(WorldId.Parse("region-bound-world"), 10, CancellationToken.None);
        Assert.Empty(commands);
    }

    [Fact]
    public async Task ACharterOverACellThisWorldDoesNotHaveIsRefused()
    {
        var client = await CreateWorldAsync("region-index-world", Owner);

        // The grid is 64 by 32, so 2048 is one past its last cell.
        var body = """
            {"displayName":"Off Map","controlledSpecies":"verdant-moss","controlledRegion":[0,2048],
             "goals":["Preserve"],"goalWeights":[100],"taboos":[],
             "actionBudget":10,"budgetRenewalPerChapter":5,"impactCeilingPerChapter":20}
            """;

        using var response = await client.SendAsync(Request(
            HttpMethod.Put,
            "/api/worlds/region-index-world/wardens/off-map-warden/charter",
            body,
            "region-index-1",
            Owner,
            Scope));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OwnershipIsNeverProjectedIntoASpectatorResponse()
    {
        var client = await CreateWorldAsync("hidden-actor-world", Owner);

        var summary = await client.GetStringAsync("/api/worlds/hidden-actor-world");
        var list = await client.GetStringAsync("/api/worlds");

        Assert.DoesNotContain(Owner, summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Owner, list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", list, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheClientConfigurationAdvertisesSignInWithoutAnySecret()
    {
        var client = _factory.CreateClient();

        var config = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/config");

        var auth = config.GetProperty("auth");
        Assert.Equal(AuthenticatedApiFactory.TenantId, auth.GetProperty("tenantId").GetString());
        Assert.Equal(AuthenticatedApiFactory.ClientId, auth.GetProperty("clientId").GetString());
        Assert.Equal($"api://{AuthenticatedApiFactory.ClientId}/{Scope}", auth.GetProperty("scope").GetString());

        var raw = await client.GetStringAsync("/api/config");
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignInIsTheOnlyForeignOriginTheContentSecurityPolicyAllows()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("connect-src 'self' https://login.microsoftonline.com;", policy, StringComparison.Ordinal);

        // The sign-in redirect is a full-page navigation, so the directory never needs to be framed.
        Assert.Contains("frame-src 'none';", policy, StringComparison.Ordinal);
        Assert.Contains("default-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpectatorReadsStayAnonymous()
    {
        await CreateWorldAsync("anonymous-read-world", Owner);
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/worlds/anonymous-read-world");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
