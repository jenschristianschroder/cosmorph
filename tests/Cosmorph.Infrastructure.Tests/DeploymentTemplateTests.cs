using System.Text.RegularExpressions;
using Cosmorph.Infrastructure.Blob;

namespace Cosmorph.Infrastructure.Tests;

/// <summary>
/// Deployment assertions. These run offline against the templates in the repository so that a future
/// edit cannot quietly re-enable shared-key access, public Storage networking or stored credentials.
/// </summary>
public sealed class DeploymentTemplateTests
{
    private static readonly string InfraRoot = Path.Combine(RepositoryRoot(), "infra");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cosmorph.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string ReadTemplate(string relativePath)
    {
        var path = Path.Combine(InfraRoot, relativePath);
        Assert.True(File.Exists(path), $"Missing template {relativePath}.");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> AllTemplates() =>
        Directory.EnumerateFiles(InfraRoot, "*.bicep*", SearchOption.AllDirectories);

    [Theory]
    [InlineData("allowSharedKeyAccess: false")]
    [InlineData("allowBlobPublicAccess: false")]
    [InlineData("publicNetworkAccess: 'Disabled'")]
    [InlineData("supportsHttpsTrafficOnly: true")]
    [InlineData("minimumTlsVersion: 'TLS1_2'")]
    [InlineData("defaultAction: 'Deny'")]
    public void StorageAccountKeepsMandatoryHardening(string requiredSetting)
    {
        var storage = ReadTemplate(Path.Combine("modules", "storage.bicep"));

        Assert.Contains(requiredSetting, storage, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageExposesOnlyABlobPrivateEndpointWithDnsIntegration()
    {
        var storage = ReadTemplate(Path.Combine("modules", "storage.bicep"));

        Assert.Contains("Microsoft.Network/privateEndpoints", storage, StringComparison.Ordinal);
        Assert.Contains("groupIds: ['blob']", storage, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Network/privateDnsZones/virtualNetworkLinks", storage, StringComparison.Ordinal);
        Assert.Contains("privatelink.blob.", storage, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Network/privateEndpoints/privateDnsZoneGroups", storage, StringComparison.Ordinal);

        // Unused Storage services must not get their own endpoints: each one is a recurring cost.
        Assert.DoesNotContain("'queue'", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("'table'", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("'file'", storage, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationConfigurationUsesThePublicBlobHostname()
    {
        var storage = ReadTemplate(Path.Combine("modules", "storage.bicep"));

        // Private DNS resolves the normal hostname; configuring the privatelink hostname would break
        // token audiences.
        Assert.Contains("output blobServiceUri string = storageAccount.properties.primaryEndpoints.blob", storage, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimesUseSystemAssignedIdentities()
    {
        foreach (var template in new[] { "webApp.bicep", "tickJob.bicep" })
        {
            var content = ReadTemplate(Path.Combine("modules", template));

            Assert.Contains("type: 'SystemAssigned'", content, StringComparison.Ordinal);
            Assert.Contains("identity: 'system'", content, StringComparison.Ordinal);
            Assert.DoesNotContain("UserAssigned", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RoleAssignmentsAreDeterministicAndLeastPrivilege()
    {
        var main = ReadTemplate("main.bicep");
        var storageRole = ReadTemplate(Path.Combine("modules", "storageRoleAssignment.bicep"));

        Assert.Contains("name: guid(storageAccount.id, principalId, roleDefinitionId)", storageRole, StringComparison.Ordinal);

        // Storage Blob Data Contributor for both runtime identities: the narrowest built-in role that
        // can write a blob, which the API needs once an authenticated caller can create a world.
        Assert.Contains("ba92f5b4-2d11-453d-a403-e96b0029c9fe", main, StringComparison.Ordinal);

        // Owner and Contributor must never be granted to a runtime identity.
        Assert.DoesNotContain("8e3af657-a8ff-443c-a75c-2fe8c4bcb635", main, StringComparison.Ordinal);
        Assert.DoesNotContain("b24988ac-6180-42a0-ab88-20f7382dd24c", main, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationConfigurationIsPlainNonSecretSettings()
    {
        var webApp = ReadTemplate(Path.Combine("modules", "webApp.bicep"));
        var main = ReadTemplate("main.bicep");

        // A tenant and an application identifier reach the container as ordinary environment
        // variables, because they are not secrets and the registration has no credential to protect.
        Assert.Contains("Cosmorph__Authentication__TenantId", webApp, StringComparison.Ordinal);
        Assert.Contains("Cosmorph__Authentication__ClientId", webApp, StringComparison.Ordinal);
        Assert.Contains("authTenantId: authTenantId", main, StringComparison.Ordinal);
        Assert.Contains("authClientId: authClientId", main, StringComparison.Ordinal);

        // Nothing about sign-in may become a secret parameter or a secret reference.
        Assert.DoesNotContain("@secure()\nparam authTenantId", webApp, StringComparison.Ordinal);
        Assert.DoesNotContain("authClientSecret", webApp, StringComparison.Ordinal);
        Assert.DoesNotContain("authClientSecret", main, StringComparison.Ordinal);
        Assert.DoesNotContain("secretRef", webApp, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryHasNoAdminCredentials()
    {
        var registry = ReadTemplate(Path.Combine("modules", "registry.bicep"));

        Assert.Contains("adminUserEnabled: false", registry, StringComparison.Ordinal);
        Assert.Contains("anonymousPullEnabled: false", registry, StringComparison.Ordinal);
        Assert.Contains("name: 'Basic'", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiScalesToZeroAndTheJobRunsOnASchedule()
    {
        var webApp = ReadTemplate(Path.Combine("modules", "webApp.bicep"));
        var tickJob = ReadTemplate(Path.Combine("modules", "tickJob.bicep"));

        Assert.Contains("minReplicas: 0", webApp, StringComparison.Ordinal);
        Assert.Contains("allowInsecure: false", webApp, StringComparison.Ordinal);
        Assert.Contains("triggerType: 'Schedule'", tickJob, StringComparison.Ordinal);

        // Overlapping executions would double-advance worlds.
        Assert.Contains("parallelism: 1", tickJob, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("listKeys(")]
    [InlineData("connectionString")]
    [InlineData("AccountKey")]
    [InlineData("SharedAccessSignature")]
    [InlineData("@secure()")]
    [InlineData("Microsoft.KeyVault")]
    public void TemplatesNeverIntroduceCredentials(string forbidden)
    {
        foreach (var path in AllTemplates())
        {
            var content = File.ReadAllText(path);

            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WorkflowsUseOidcAndNeverStoreAzureCredentials()
    {
        var workflowDirectory = Path.Combine(RepositoryRoot(), ".github", "workflows");
        Assert.True(Directory.Exists(workflowDirectory), "Expected at least one workflow.");

        var workflows = Directory.EnumerateFiles(workflowDirectory, "*.yml").ToList();
        Assert.NotEmpty(workflows);

        foreach (var path in workflows)
        {
            var content = File.ReadAllText(path);

            Assert.DoesNotContain("creds:", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("client-secret", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publish-profile", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AZURE_CREDENTIALS", content, StringComparison.Ordinal);

            if (content.Contains("azure/login", StringComparison.Ordinal))
            {
                Assert.Contains("id-token: write", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DeploymentWorkflowPinsThirdPartyActionsToCommitShas()
    {
        var workflowDirectory = Path.Combine(RepositoryRoot(), ".github", "workflows");

        foreach (var path in Directory.EnumerateFiles(workflowDirectory, "*.yml"))
        {
            foreach (Match match in Regex.Matches(
                File.ReadAllText(path),
                @"uses:\s*(?<ref>[^\s#]+@[^\s#]+)",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            {
                var reference = match.Groups["ref"].Value;
                var version = reference[(reference.IndexOf('@', StringComparison.Ordinal) + 1)..];

                Assert.True(
                    version.Length == 40 && version.All(Uri.IsHexDigit),
                    $"{Path.GetFileName(path)} must pin {reference} to a full commit SHA.");
            }
        }
    }

    [Fact]
    public void ContainerImagesRunAsNonRootWithoutDiagnostics()
    {
        var root = RepositoryRoot();
        foreach (var path in new[]
        {
            Path.Combine(root, "src", "Cosmorph.Api", "Dockerfile"),
            Path.Combine(root, "src", "Cosmorph.TickJob", "Dockerfile"),
        })
        {
            Assert.True(File.Exists(path), $"Missing {path}.");
            var content = File.ReadAllText(path);

            Assert.Contains("USER $APP_UID", content, StringComparison.Ordinal);
            Assert.DoesNotContain("USER root", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StorageContainerNameMatchesTheApplication()
    {
        // A mismatch deploys cleanly and then fails at runtime on every blob call with
        // ContainerNotFound, because the container the template creates is not the one the
        // runtimes open. Nothing else in the deployment catches it.
        var template = ReadTemplate(Path.Combine("modules", "storage.bicep"));
        var match = Regex.Match(template, @"param\s+containerName\s+string\s*=\s*'(?<name>[^']+)'");

        Assert.True(match.Success, "storage.bicep must declare a default containerName.");
        Assert.Equal(BlobPaths.ContainerName, match.Groups["name"].Value);
    }
}
