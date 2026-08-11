using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cosmorph.Api.Tests;

/// <summary>
/// A production process must refuse to start with a fake Worldmind or an in-memory store, and there
/// is no development bypass that could activate in Production.
/// </summary>
public sealed class ProductionStartupTests
{
    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.UseSetting("environment", "Production");
            builder.UseEnvironment("Production");
        }
    }

    /// <summary>
    /// Everything a production process needs except authentication, so the only thing left to reject
    /// is the missing directory configuration.
    /// </summary>
    private sealed class KeylessProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.UseSetting("environment", "Production");
            builder.UseEnvironment("Production");
            builder.UseSetting("Cosmorph:UseInMemoryStore", "false");
            builder.UseSetting("Cosmorph:UseFakeWorldmind", "false");
            builder.UseSetting("Cosmorph:StorageBlobServiceUri", "https://example.blob.core.windows.net");
            builder.UseSetting("Cosmorph:ModelEndpoint", "https://example.openai.azure.com");
            builder.UseSetting("Cosmorph:ModelDeployment", "gpt-test");
        }
    }

    [Fact]
    public void ProductionRefusesToStartWithoutKeylessAzureConfiguration()
    {
        // Nothing in the repository configures a production process, and local-mode settings are
        // exactly what production validation rejects, so startup must fail rather than degrade.
        using var factory = new ProductionFactory();

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Production", Unwrap(failure).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRefusesToStartWithoutAuthentication()
    {
        // Serving every mutation as a permanent 501 would read like a deployment fault. A production
        // process that cannot authenticate anyone fails fast instead.
        using var factory = new KeylessProductionFactory();

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Entra authentication", Unwrap(failure).Message, StringComparison.Ordinal);
    }

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate ? Unwrap(aggregate.InnerException!) : exception;
}
