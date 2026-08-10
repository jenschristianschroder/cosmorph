using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmorph.Api.Tests;

/// <summary>
/// Anonymous spectator reads: only public worlds are reachable, limits are validated before
/// allocation and responses carry no private configuration or internal detail.
/// </summary>
public sealed class SpectatorEndpointTests : IClassFixture<CosmorphApiFactory>
{
    private readonly CosmorphApiFactory _factory;

    public SpectatorEndpointTests(CosmorphApiFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthProbesRevealNoConfiguration()
    {
        var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("blob", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicWorldsAreListedWithTheConfiguredWorldmindMode()
    {
        var client = await _factory.CreateWorldAsync("listed-world");

        var payload = await client.GetFromJsonAsync<JsonElement>("/api/worlds");

        Assert.Equal("FakeGameMaster", payload.GetProperty("worldmindMode").GetString());
        Assert.Contains(
            payload.GetProperty("worlds").EnumerateArray(),
            world => world.GetProperty("worldId").GetString() == "listed-world");
    }

    [Fact]
    public async Task PrivateWorldsAreNeitherListedNorReadable()
    {
        var client = await _factory.CreateWorldAsync("hidden-world", isPublic: false);

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/worlds");
        var summary = await client.GetAsync("/api/worlds/hidden-world");
        var snapshot = await client.GetAsync("/api/worlds/hidden-world/snapshot");
        var events = await client.GetAsync("/api/worlds/hidden-world/events");

        Assert.DoesNotContain(
            listed.GetProperty("worlds").EnumerateArray(),
            world => world.GetProperty("worldId").GetString() == "hidden-world");
        Assert.Equal(HttpStatusCode.NotFound, summary.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, snapshot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
    }

    [Fact]
    public async Task AWorldThatExistsAndOneThatDoesNotAreIndistinguishable()
    {
        var client = await _factory.CreateWorldAsync("hidden-world", isPublic: false);

        var hidden = await client.GetAsync("/api/worlds/hidden-world");
        var missing = await client.GetAsync("/api/worlds/no-such-world");
        var invalid = await client.GetAsync("/api/worlds/NOT_A_VALID_ID");

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        // The bodies differ only by the per-request trace identifier.
        Assert.Equal(WithoutTraceId(await hidden.Content.ReadAsStringAsync()), WithoutTraceId(await missing.Content.ReadAsStringAsync()));
        Assert.Equal(WithoutTraceId(await hidden.Content.ReadAsStringAsync()), WithoutTraceId(await invalid.Content.ReadAsStringAsync()));
    }

    private static string WithoutTraceId(string body)
    {
        var document = JsonDocument.Parse(body);
        return string.Join(
            ';',
            document.RootElement.EnumerateObject()
                .Where(p => p.Name is not "traceId")
                .Select(p => p.Name + "=" + p.Value.ToString()));
    }

    [Fact]
    public async Task ASummaryIsAPresentationDtoWithoutPrivateConfiguration()
    {
        var client = await _factory.CreateWorldAsync("summary-world");

        var response = await client.GetAsync("/api/worlds/summary-world");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"healthPermille\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("warden", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("worlds/", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConditionalReadsAreSupported()
    {
        var client = await _factory.CreateWorldAsync("etag-world");

        var first = await client.GetAsync("/api/worlds/etag-world");
        var etag = first.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrEmpty(etag));

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/etag-world");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task SnapshotArraysMatchTheAdvertisedGrid()
    {
        var client = await _factory.CreateWorldAsync("snapshot-world");

        var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/worlds/snapshot-world/snapshot");

        var width = snapshot.GetProperty("gridWidth").GetInt32();
        var height = snapshot.GetProperty("gridHeight").GetInt32();
        Assert.Equal(width * height, snapshot.GetProperty("cells").GetProperty("biome").GetArrayLength());
        Assert.Equal(width * height, snapshot.GetProperty("cells").GetProperty("vitalityPermille").GetArrayLength());
        Assert.Equal("spectator-snapshot/1", snapshot.GetProperty("schema").GetString());
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=201")]
    [InlineData("?limit=-1")]
    [InlineData("?after=-5")]
    public async Task EventPagingLimitsAreValidatedBeforeAnythingIsAllocated(string query)
    {
        var client = await _factory.CreateWorldAsync("events-world");

        var response = await client.GetAsync("/api/worlds/events-world/events" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventPagesAreCursorBased()
    {
        var client = await _factory.CreateWorldAsync("events-world");

        var page = await client.GetFromJsonAsync<JsonElement>("/api/worlds/events-world/events?after=0&limit=10");

        Assert.True(page.GetProperty("events").GetArrayLength() <= 10);
        Assert.True(page.GetProperty("cursor").GetInt64() >= 0);
        Assert.True(page.GetProperty("latestSequence").GetInt64() >= 0);
    }

    [Fact]
    public async Task ResponsesCarrySafeRenderingHeaders()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal("nosniff", string.Join(string.Empty, response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(string.Empty, response.Headers.GetValues("X-Frame-Options")));
        var policy = string.Join(string.Empty, response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUntrustedWorldNameIsReturnedAsDataNotMarkup()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/worlds")
        {
            Content = CosmorphApiFactory.JsonBody(
                """{"worldId":"escaping-world","name":"<script>alert(1)</script>","seed":7,"isPublic":true}"""),
        };
        request.Headers.Add("Idempotency-Key", "create-escaping-world");
        using var created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var response = await client.GetAsync("/api/worlds/escaping-world");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>", body, StringComparison.Ordinal);
        Assert.Contains("\\u003Cscript\\u003E", body, StringComparison.Ordinal);
    }
}
