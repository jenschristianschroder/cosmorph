using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmorph.Api.Tests;

/// <summary>
/// The block of places around one place: anonymous and conditional like the rest of the spectator
/// surface, bounded in reach, and carrying no Warden configuration.
/// </summary>
public sealed class NeighbourhoodEndpointTests : IClassFixture<CosmorphApiFactory>
{
    private readonly CosmorphApiFactory _factory;

    public NeighbourhoodEndpointTests(CosmorphApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ABlockCarriesWholePlacesAndTheLayoutToArrangeThem()
    {
        var client = await _factory.CreateWorldAsync("block-world");

        var block = await client.GetFromJsonAsync<JsonElement>(
            "/api/worlds/block-world/cells/900/neighbourhood?radius=1");

        Assert.Equal("spectator-neighbourhood/1", block.GetProperty("schema").GetString());
        Assert.Equal("block-world", block.GetProperty("worldId").GetString());
        Assert.Equal(900, block.GetProperty("centerCellIndex").GetInt32());
        Assert.Equal(3, block.GetProperty("rows").GetInt32());
        Assert.Equal(3, block.GetProperty("columns").GetInt32());

        var cells = block.GetProperty("cells");
        Assert.Equal(9, cells.GetArrayLength());

        // Each item is a whole place, so the browser has one parser for a place however it was read.
        var first = cells[0];
        foreach (var name in new[] { "cellIndex", "biome", "timber", "stone", "fibre", "dominantStress", "species" })
        {
            Assert.True(first.TryGetProperty(name, out _), name + " is missing from a place in the block.");
        }
    }

    [Fact]
    public async Task AReachThatIsNotOfferedIsRefusedRatherThanTrimmed()
    {
        var client = await _factory.CreateWorldAsync("block-world");

        var tooWide = await client.GetAsync("/api/worlds/block-world/cells/900/neighbourhood?radius=3");
        var negative = await client.GetAsync("/api/worlds/block-world/cells/900/neighbourhood?radius=-1");

        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task ABlockIsReadConditionallyAndItsTagNamesBothThePlaceAndTheReach()
    {
        var client = await _factory.CreateWorldAsync("block-etag-world");

        var first = await client.GetAsync("/api/worlds/block-etag-world/cells/12/neighbourhood?radius=1");
        var etag = first.Headers.ETag?.Tag;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(string.IsNullOrEmpty(etag));

        using var conditional = new HttpRequestMessage(
            HttpMethod.Get, "/api/worlds/block-etag-world/cells/12/neighbourhood?radius=1");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);

        // A wider block is a different payload, so it is never served from the narrower one's entry.
        using var wider = new HttpRequestMessage(
            HttpMethod.Get, "/api/worlds/block-etag-world/cells/12/neighbourhood?radius=2");
        wider.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(wider)).StatusCode);
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("-1")]
    public async Task ACentreOutsideTheGridIsAnsweredExactlyLikeAnUnknownWorld(string cellIndex)
    {
        var client = await _factory.CreateWorldAsync("block-world");

        var outOfRange = await client.GetAsync($"/api/worlds/block-world/cells/{cellIndex}/neighbourhood");
        var unknownWorld = await client.GetAsync("/api/worlds/no-such-world/cells/0/neighbourhood");

        Assert.Equal(HttpStatusCode.NotFound, outOfRange.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownWorld.StatusCode);
        Assert.Equal(
            WithoutTraceId(await outOfRange.Content.ReadAsStringAsync()),
            WithoutTraceId(await unknownWorld.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ABlockCarriesNoWardenConfiguration()
    {
        var client = await _factory.CreateWorldAsync("block-world");

        var body = await client.GetStringAsync("/api/worlds/block-world/cells/0/neighbourhood?radius=2");

        Assert.DoesNotContain("warden", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charter", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APrivateWorldHasNoReadableBlocks()
    {
        var client = await _factory.CreateWorldAsync("block-hidden-world", isPublic: false);

        var response = await client.GetAsync("/api/worlds/block-hidden-world/cells/0/neighbourhood");

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
