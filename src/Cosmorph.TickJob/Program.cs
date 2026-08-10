using System.Globalization;
using Cosmorph.Application.Scheduling;
using Cosmorph.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Cosmorph.TickJob;

// The TickJob is the only normal writer of simulation outcomes. One execution drains a bounded
// number of minute buckets and worlds, then exits; the schedule keeps the remainder durable.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.UseUtcTimestamp = true;
});

var isProduction = builder.Environment.IsProduction();
builder.Services.AddCosmorph(builder.Configuration, isProduction);

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Cosmorph.TickJob");
var runner = host.Services.GetRequiredService<TickRunner>();

using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(10));
using var console = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    console.Cancel();
};

using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, console.Token);

// A local loop makes it possible to watch worlds evolve without Azure. Production always uses the
// scheduled Container Apps Job instead.
var loop = !isProduction
    && string.Equals(
        builder.Configuration["Cosmorph:LoopLocally"],
        "true",
        StringComparison.OrdinalIgnoreCase);

var exitCode = 0;
do
{
    try
    {
        var summary = await runner.RunOnceAsync(linked.Token).ConfigureAwait(false);
        JobLog.RunComplete(
            logger,
            summary.BucketsProcessed,
            summary.WorldsAdvanced,
            summary.WorldsSkipped,
            summary.DirectTicks,
            summary.CompressedTicks,
            summary.ModelCalls,
            summary.Watermark.ToString("O", CultureInfo.InvariantCulture));
    }
    catch (OperationCanceledException)
    {
        break;
    }
#pragma warning disable CA1031 // A job execution must exit with a status rather than crash the schedule.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        JobLog.RunFailed(logger, ex);
        exitCode = 1;
        break;
    }

    if (loop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}
while (loop && !linked.IsCancellationRequested);

return exitCode;
