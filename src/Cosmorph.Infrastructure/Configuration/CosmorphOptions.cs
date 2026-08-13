using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using Cosmorph.Application.Abstractions;
using Cosmorph.Application.Scheduling;
using Cosmorph.Application.Worldmind;
using Cosmorph.Application.Worlds;
using Cosmorph.Infrastructure.Blob;
using Cosmorph.Infrastructure.Files;
using Cosmorph.Infrastructure.Memory;
using Cosmorph.Infrastructure.Worldmind;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cosmorph.Infrastructure.Configuration;

/// <summary>
/// Non-secret runtime configuration. Resource names, endpoint URIs, deployment names and feature
/// flags are not secrets; credentials are never part of configuration.
/// </summary>
public sealed class CosmorphOptions
{
    public const string SectionName = "Cosmorph";

    /// <summary>Binds the configuration section. Callers that need a value before the container is
    /// built, such as authentication wiring, use this rather than resolving a service.</summary>
    public static CosmorphOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CosmorphOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }

    /// <summary>Blob service URI, for example https://example.blob.core.windows.net.</summary>
    public string? StorageBlobServiceUri { get; set; }

    /// <summary>Azure model endpoint URI. Access uses a managed identity, never a key.</summary>
    public string? ModelEndpoint { get; set; }

    public string? ModelDeployment { get; set; }

    /// <summary>Uses a local store instead of Azure Blob Storage. Local development only.</summary>
    public bool UseInMemoryStore { get; set; }

    /// <summary>
    /// Local-development directory shared by the API and the TickJob. When set, the local store is
    /// file-backed so both processes observe the same worlds; otherwise it is process-local memory.
    /// A relative value is resolved under the system temporary directory so both processes agree
    /// regardless of their working directory.
    /// </summary>
    public string? LocalStorePath { get; set; }

    public string? ResolvedLocalStorePath =>
        string.IsNullOrWhiteSpace(LocalStorePath)
            ? null
            : Path.IsPathRooted(LocalStorePath)
                ? LocalStorePath
                : Path.Combine(Path.GetTempPath(), LocalStorePath);

    /// <summary>Uses the deterministic FakeGameMaster. Local development and tests only.</summary>
    public bool UseFakeWorldmind { get; set; }

    /// <summary>Seeds demo worlds into the in-memory store at startup.</summary>
    public bool SeedDemoWorlds { get; set; }

    /// <summary>
    /// Lets a signed-in actor take ownership of a world that nobody owns, which is how a world
    /// created before ownership existed becomes configurable again. Off unless an environment asks
    /// for it, and it can never take a world away from an owner: the only transition is none → you.
    /// </summary>
    public bool AllowAdoptingUnownedWorlds { get; set; }

    public SimulationOptions Simulation { get; set; } = new();

    /// <summary>Entra ID settings for authenticated mutations. Empty means mutations stay closed.</summary>
    public AuthenticationOptions Authentication { get; set; } = new();

    /// <summary>
    /// Fails closed in Production: a fake Worldmind, an in-memory store or anything that looks like a
    /// credential must never start a production process.
    /// </summary>
    public void ValidateForEnvironment(bool isProduction)
    {
        Simulation.Validate();

        if (!isProduction)
        {
            return;
        }

        if (UseFakeWorldmind)
        {
            throw new InvalidOperationException("Production must not run with the fake Worldmind.");
        }

        if (UseInMemoryStore)
        {
            throw new InvalidOperationException("Production must not run with the in-memory world store.");
        }

        if (!Uri.TryCreate(StorageBlobServiceUri, UriKind.Absolute, out var storage) || storage.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("An HTTPS Blob service URI is required in Production.");
        }

        if (LooksLikeCredential(StorageBlobServiceUri) || LooksLikeCredential(ModelEndpoint))
        {
            throw new InvalidOperationException("Credential-like configuration is not allowed. Use managed identity.");
        }

        if (!Uri.TryCreate(ModelEndpoint, UriKind.Absolute, out var model) || model.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("An HTTPS model endpoint is required in Production.");
        }

        if (string.IsNullOrWhiteSpace(ModelDeployment))
        {
            throw new InvalidOperationException("A model deployment name is required in Production.");
        }
    }

    internal static bool LooksLikeCredential(string? value) =>
        value is not null
        && (value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase)
            || value.Contains("sig=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Composition root shared by the API and the TickJob.</summary>
public static class CosmorphServiceCollectionExtensions
{
    public static IServiceCollection AddCosmorph(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = CosmorphOptions.FromConfiguration(configuration);
        options.ValidateForEnvironment(isProduction);
        services.AddSingleton(options);
        services.AddSingleton(options.Simulation);
        services.AddSingleton(options.Authentication);
        services.AddSingleton<IClock, SystemClock>();

        if (options.UseInMemoryStore && options.ResolvedLocalStorePath is { } localPath)
        {
            services.AddSingleton(_ => new FileSystemWorldStore(localPath));
            services.AddSingleton<IWorldStore>(sp => sp.GetRequiredService<FileSystemWorldStore>());
            services.AddSingleton<IWorldSchedule>(sp => sp.GetRequiredService<FileSystemWorldStore>());
        }
        else if (options.UseInMemoryStore)
        {
            services.AddSingleton<InMemoryWorldStore>();
            services.AddSingleton<IWorldStore>(sp => sp.GetRequiredService<InMemoryWorldStore>());
            services.AddSingleton<IWorldSchedule, InMemoryWorldSchedule>();
        }
        else
        {
            services.AddSingleton(_ =>
            {
                var uri = new Uri(options.StorageBlobServiceUri
                    ?? throw new InvalidOperationException("StorageBlobServiceUri is required."));

                // Keyless by construction: a token credential is the only supported authentication.
                return new BlobServiceClient(uri, new DefaultAzureCredential());
            });

            services.AddSingleton(sp => sp.GetRequiredService<BlobServiceClient>()
                .GetBlobContainerClient(BlobPaths.ContainerName));
            services.AddSingleton<IWorldStore>(sp => new BlobWorldStore(sp.GetRequiredService<BlobContainerClient>()));
            services.AddSingleton<IWorldSchedule>(sp => new BlobWorldSchedule(sp.GetRequiredService<BlobContainerClient>()));
        }

        if (options.UseFakeWorldmind)
        {
            services.AddSingleton<IGameMaster, FakeGameMaster>();
        }
        else
        {
            services.AddSingleton<IGameMaster>(sp =>
            {
                var endpoint = new Uri(options.ModelEndpoint
                    ?? throw new InvalidOperationException("ModelEndpoint is required."));
                var client = new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
                return new AzureGameMaster(
                    client,
                    options.ModelDeployment ?? throw new InvalidOperationException("ModelDeployment is required."),
                    sp.GetRequiredService<ILogger<AzureGameMaster>>());
            });
        }

        services.AddSingleton<WorldFactory>();
        services.AddSingleton<WorldAdvancer>();
        services.AddSingleton<TickRunner>();

        return services;
    }
}
