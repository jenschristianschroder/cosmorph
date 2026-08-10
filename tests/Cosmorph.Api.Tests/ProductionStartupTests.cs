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

    [Fact]
    public void ProductionRefusesToStartWithoutKeylessAzureConfiguration()
    {
        // Nothing in the repository configures a production process, and local-mode settings are
        // exactly what production validation rejects, so startup must fail rather than degrade.
        using var factory = new ProductionFactory();

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Production", Unwrap(failure).Message, StringComparison.Ordinal);
    }

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate ? Unwrap(aggregate.InnerException!) : exception;
}
