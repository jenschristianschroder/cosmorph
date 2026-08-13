using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmorph.Api.Tests;

/// <summary>
/// The per-place read: anonymous like the rest of the spectator surface, conditional like the rest of
/// it, and carrying no Warden configuration.
/// </summary>
public sealed class CellDetailEndpointTests : IClassFixture<CosmorphApiFactory>
{
    private readonly CosmorphApiFactory _factory;

    public CellDetailEndpointTests(CosmorphApiFactory factory) => _factory = factory;

    [Fact]
    public async Task APlaceDescribesItsTerrainClimateMaterialsAndPeople()
    {
        var client = await _factory.CreateWorldAsync("place-world");

        var detail = await client.GetFromJsonAsync<JsonElement>("/api/worlds/place-world/cells/900");

        Assert.Equal("spectator-cell/1", detail.GetProperty("schema").GetString());
        Assert.Equal("place-world", detail.GetProperty("worldId").GetString());
        Assert.Equal(900, detail.GetProperty("cellIndex").GetInt32());
        Assert.InRange(detail.GetProperty("latitudeDegrees").GetInt32(), -90, 90);
        Assert.InRange(detail.GetProperty("longitudeDegrees").GetInt32(), -180, 180);
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("biome").GetString()));
        Assert.InRange(detail.GetProperty("vitalityPermille").GetInt32(), 0, 1000);
        Assert.InRange(detail.GetProperty("resourceRichnessPermille").GetInt32(), 0, 1000);

        // Present whether or not anything stands there or lives there, so the panel never has to guess.
        foreach (var name in new[] { "timber", "stone", "fibre", "timberYield", "droughtPermille", "species" })
        {
            Assert.True(detail.TryGetProperty(name, out _), name + " is missing from the payload.");
        }

        Assert.Equal(JsonValueKind.Array, detail.GetProperty("species").ValueKind);
    }

    [Fact]
    public async Task APlaceCarriesNoWardenConfiguration()
    {
        var client = await _factory.CreateWorldAsync("place-world");

        var body = await client.GetStringAsync("/api/worlds/place-world/cells/0");

        Assert.DoesNotContain("warden", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charter", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APlaceIsReadConditionally()
    {
        var client = await _factory.CreateWorldAsync("place-etag-world");

        var first = await client.GetAsync("/api/worlds/place-etag-world/cells/12");
        var etag = first.Headers.ETag?.Tag;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(string.IsNullOrEmpty(etag));

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/place-etag-world/cells/12");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

        // The tag names the cell, so a different place is not served from the same cache entry.
        using var otherCell = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/place-etag-world/cells/13");
        otherCell.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var third = await client.SendAsync(otherCell);

        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("-1")]
    public async Task AnIndexOutsideTheGridIsAnsweredExactlyLikeAnUnknownWorld(string cellIndex)
    {
        var client = await _factory.CreateWorldAsync("place-world");

        var outOfRange = await client.GetAsync("/api/worlds/place-world/cells/" + cellIndex);
        var unknownWorld = await client.GetAsync("/api/worlds/no-such-world/cells/0");

        Assert.Equal(HttpStatusCode.NotFound, outOfRange.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownWorld.StatusCode);
        Assert.Equal(
            WithoutTraceId(await outOfRange.Content.ReadAsStringAsync()),
            WithoutTraceId(await unknownWorld.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task APrivateWorldHasNoReadablePlaces()
    {
        var client = await _factory.CreateWorldAsync("place-hidden-world", isPublic: false);

        var response = await client.GetAsync("/api/worlds/place-hidden-world/cells/0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
}
