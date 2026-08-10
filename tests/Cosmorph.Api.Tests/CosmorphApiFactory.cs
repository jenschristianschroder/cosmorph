using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cosmorph.Api.Tests;

/// <summary>
/// Hosts the API in local mode: an in-memory store and the deterministic Worldmind, so no test needs
/// Azure, a network or a live model.
/// </summary>
public sealed class CosmorphApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Local-mode settings are supplied as environment variables so they take precedence over the
    /// application's own appsettings files, and the Testing environment keeps the developer's
    /// file-backed local store out of the tests.
    /// </summary>
    static CosmorphApiFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Cosmorph__UseInMemoryStore", "true");
        Environment.SetEnvironmentVariable("Cosmorph__UseFakeWorldmind", "true");
        Environment.SetEnvironmentVariable("Cosmorph__SeedDemoWorlds", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
    }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Creates a public world through the ordinary validated mutation path.</summary>
    public async Task<HttpClient> CreateWorldAsync(string worldId, ulong seed = 4242UL, bool isPublic = true)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/worlds")
        {
            Content = JsonContent.Create(new { worldId, name = "Test World", seed, isPublic }),
        };
        request.Headers.Add("Idempotency-Key", "create-" + worldId);

        using var response = await client.SendAsync(request);
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
        {
            throw new InvalidOperationException(
                $"World creation failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return client;
    }

    public static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}
