using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Worlds;
using Cosmorph.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cosmorph.Infrastructure.Tests;

/// <summary>
/// Production must fail closed: a fake Worldmind, an in-memory store, a plaintext endpoint or
/// anything that looks like a credential must never start a production process.
/// </summary>
public sealed class CosmorphOptionsTests
{
    private static CosmorphOptions Production() => new()
    {
        StorageBlobServiceUri = "https://example.blob.core.windows.net",
        ModelEndpoint = "https://example.openai.azure.com",
        ModelDeployment = "worldmind",
        UseInMemoryStore = false,
        UseFakeWorldmind = false,
    };

    [Fact]
    public void AValidProductionConfigurationIsAccepted() =>
        Production().ValidateForEnvironment(isProduction: true);

    [Fact]
    public void ProductionRejectsTheFakeWorldmind()
    {
        var options = Production();
        options.UseFakeWorldmind = true;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Fact]
    public void ProductionRejectsTheInMemoryStore()
    {
        var options = Production();
        options.UseInMemoryStore = true;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.blob.core.windows.net")]
    [InlineData("example.blob.core.windows.net")]
    public void ProductionRequiresAnHttpsStorageEndpoint(string? uri)
    {
        var options = Production();
        options.StorageBlobServiceUri = uri;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://example.openai.azure.com")]
    public void ProductionRequiresAnHttpsModelEndpoint(string? uri)
    {
        var options = Production();
        options.ModelEndpoint = uri;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Fact]
    public void ProductionRequiresAModelDeploymentName()
    {
        var options = Production();
        options.ModelDeployment = "  ";

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Theory]
    [InlineData("https://example.blob.core.windows.net/?sig=abc")]
    [InlineData("https://example.blob.core.windows.net/?SharedAccessSignature=abc")]
    [InlineData("https://example.blob.core.windows.net/?AccountKey=abc")]
    public void ProductionRejectsCredentialLikeStorageConfiguration(string uri)
    {
        var options = Production();
        options.StorageBlobServiceUri = uri;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Theory]
    [InlineData("https://example.openai.azure.com/?api-key=abc")]
    [InlineData("https://example.openai.azure.com/?sig=abc")]
    public void ProductionRejectsCredentialLikeModelConfiguration(string uri)
    {
        var options = Production();
        options.ModelEndpoint = uri;

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Fact]
    public void ProductionRejectsOutOfRangeSimulationOptions()
    {
        var options = Production();
        options.Simulation = new SimulationOptions { RealTimePerTick = TimeSpan.Zero };

        Assert.Throws<InvalidOperationException>(() => options.ValidateForEnvironment(isProduction: true));
    }

    [Fact]
    public void LocalDevelopmentMayUseTheFakeWorldmindAndLocalStore()
    {
        var options = new CosmorphOptions { UseFakeWorldmind = true, UseInMemoryStore = true };

        options.ValidateForEnvironment(isProduction: false);
    }

    [Fact]
    public void ARelativeLocalStorePathIsResolvedSoBothProcessesAgree()
    {
        var options = new CosmorphOptions { LocalStorePath = "cosmorph-local" };

        Assert.Equal(Path.Combine(Path.GetTempPath(), "cosmorph-local"), options.ResolvedLocalStorePath);
        Assert.Null(new CosmorphOptions().ResolvedLocalStorePath);
    }

    [Fact]
    public void LocalCompositionUsesTheLocalStoreAndFakeWorldmind()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCosmorph(Configuration(new Dictionary<string, string?>
        {
            ["Cosmorph:UseInMemoryStore"] = "true",
            ["Cosmorph:UseFakeWorldmind"] = "true",
        }), isProduction: false);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<Memory.InMemoryWorldStore>(provider.GetRequiredService<IWorldStore>());
        Assert.Equal("FakeGameMaster", provider.GetRequiredService<IGameMaster>().Name);
        Assert.NotNull(provider.GetRequiredService<WorldFactory>());
    }

    [Fact]
    public void AFileBackedLocalStoreIsSharedByBothProcesses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cosmorph-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddCosmorph(Configuration(new Dictionary<string, string?>
            {
                ["Cosmorph:UseInMemoryStore"] = "true",
                ["Cosmorph:UseFakeWorldmind"] = "true",
                ["Cosmorph:LocalStorePath"] = directory,
            }), isProduction: false);

            using var provider = services.BuildServiceProvider();

            var store = provider.GetRequiredService<IWorldStore>();
            Assert.IsType<Files.FileSystemWorldStore>(store);
            Assert.Same(store, provider.GetRequiredService<IWorldSchedule>());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProductionCompositionRefusesAFakeWorldmind()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(() => services.AddCosmorph(
            Configuration(new Dictionary<string, string?>
            {
                ["Cosmorph:UseFakeWorldmind"] = "true",
                ["Cosmorph:StorageBlobServiceUri"] = "https://example.blob.core.windows.net",
            }),
            isProduction: true));
    }

    [Fact]
    public void ProductionCompositionRefusesMissingConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddCosmorph(Configuration([]), isProduction: true));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
