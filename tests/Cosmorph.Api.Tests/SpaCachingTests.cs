using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Cosmorph.Api.Tests;

/// <summary>
/// The Observatory is served by this API, so the caching directives on it are the API's
/// responsibility. The shell names the hashed asset files, which means a browser holding a cached
/// copy runs a build that has already been replaced — a deployed fix that nobody can see.
/// </summary>
public sealed class SpaCachingTests : IDisposable
{
    private const string AssetPath = "/assets/index-abc123.js";

    private readonly string webRoot = Directory.CreateTempSubdirectory("cosmorph-web").FullName;
    private readonly CosmorphApiFactory factory = new();

    public SpaCachingTests()
    {
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Cosmorph</title>");
        File.WriteAllText(Path.Combine(webRoot, "assets", "index-abc123.js"), "export const version = 1;\n");
    }

    /// <summary>
    /// "/" and any deep link reach the shell through the fallback rather than the static file
    /// middleware: the fallback endpoint is selected during routing, and static files step aside
    /// once an endpoint exists. The fallback therefore needs the same options, or it answers with no
    /// directive at all and the browser invents its own freshness from the file's age.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/worlds/quiet-meridian")]
    [InlineData("/index.html")]
    public async Task TheShellIsAlwaysRevalidated(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", CacheControl(response));
    }

    [Fact]
    public async Task AssetsAreHeldForAWeekBecauseTheirNamesCarryTheirContent()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(AssetPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=604800", CacheControl(response));
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            Directory.Delete(webRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file handle can outlive the host briefly. The path is a temporary directory, so
            // leaving it behind is not worth failing a passing test over.
        }
    }

    private HttpClient CreateClient() =>
        factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot)).CreateClient();

    private static string CacheControl(HttpResponseMessage response) =>
        string.Join(", ", response.Headers.GetValues("Cache-Control"));
}
