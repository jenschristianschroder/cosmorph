using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmorph.Api.Tests;

/// <summary>
/// What is alive on every cell, read by a viewer zoomed in far enough to see individual creatures:
/// anonymous and conditional like the rest of the spectator surface, and carrying no Warden
/// configuration.
/// </summary>
public sealed class WorldLifeEndpointTests : IClassFixture<CosmorphApiFactory>
{
    private readonly CosmorphApiFactory _factory;

    public WorldLifeEndpointTests(CosmorphApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TheLifeOfAWorldIsOneColumnPerThingTheCloseUpDraws()
    {
        var client = await _factory.CreateWorldAsync("life-world");

        var life = await client.GetFromJsonAsync<JsonElement>("/api/worlds/life-world/life");

        Assert.Equal("spectator-life/1", life.GetProperty("schema").GetString());
        Assert.Equal("life-world", life.GetProperty("worldId").GetString());

        var count = life.GetProperty("gridWidth").GetInt32() * life.GetProperty("gridHeight").GetInt32();
        foreach (var name in new[] { "elevation", "biomassPermille", "timber", "stone", "fibre" })
        {
            Assert.True(life.TryGetProperty(name, out var column), name + " is missing from the life of the world.");
            Assert.Equal(count, column.GetArrayLength());
        }

        // Sea level travels with the payload so the browser draws the coastline where the generator
        // put it, rather than at a threshold it guessed and would have to be told about again.
        Assert.InRange(life.GetProperty("seaLevel").GetInt32(), 1, 999);

        var species = life.GetProperty("species");
        Assert.True(species.GetArrayLength() > 0);
        foreach (var entry in species.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(entry.GetProperty("species").GetString()));
            Assert.False(string.IsNullOrEmpty(entry.GetProperty("displayName").GetString()));
            Assert.False(string.IsNullOrEmpty(entry.GetProperty("archetype").GetString()));
            Assert.Equal(count, entry.GetProperty("population").GetArrayLength());
        }
    }

    [Fact]
    public async Task TheLifeOfAWorldSaysNothingTheSnapshotAlreadySays()
    {
        var client = await _factory.CreateWorldAsync("life-world");

        var life = await client.GetFromJsonAsync<JsonElement>("/api/worlds/life-world/life");

        // Biome, climate, stress and constructions all reach the close-up from the snapshot. Sending
        // them twice is how the two reads would come to disagree about the same cell.
        foreach (var name in new[] { "biome", "cells", "stress", "temperature", "construction" })
        {
            Assert.False(life.TryGetProperty(name, out _), name + " is already in the snapshot.");
        }
    }

    [Fact]
    public async Task TheLifeOfAWorldIsReadConditionally()
    {
        var client = await _factory.CreateWorldAsync("life-etag-world");

        var first = await client.GetAsync("/api/worlds/life-etag-world/life");
        var etag = first.Headers.ETag?.Tag;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(string.IsNullOrEmpty(etag));

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/life-etag-world/life");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);

        // The snapshot of the same world at the same version is a different payload, so neither read
        // is ever served out of the other's cache entry.
        using var snapshot = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/life-etag-world/snapshot");
        snapshot.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(snapshot)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownWorldHasNoLifeAndIsAnsweredLikeEveryOtherUnknownWorld()
    {
        var client = await _factory.CreateWorldAsync("life-world");

        var unknownLife = await client.GetAsync("/api/worlds/no-such-world/life");
        var unknownSnapshot = await client.GetAsync("/api/worlds/no-such-world/snapshot");

        Assert.Equal(HttpStatusCode.NotFound, unknownLife.StatusCode);
        Assert.Equal(
            WithoutTraceId(await unknownLife.Content.ReadAsStringAsync()),
            WithoutTraceId(await unknownSnapshot.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task APrivateWorldHasNoReadableLife()
    {
        var client = await _factory.CreateWorldAsync("life-hidden-world", isPublic: false);

        var response = await client.GetAsync("/api/worlds/life-hidden-world/life");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheLifeOfAWorldCarriesNoWardenConfiguration()
    {
        var client = await _factory.CreateWorldAsync("life-world");

        var body = await client.GetStringAsync("/api/worlds/life-world/life");

        Assert.DoesNotContain("warden", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charter", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", body, StringComparison.OrdinalIgnoreCase);
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
