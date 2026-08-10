using Microsoft.Extensions.Logging;

namespace Cosmorph.TickJob;

/// <summary>Structured, allocation-free job logging. No secrets, prompts or user text are logged.</summary>
internal static class JobLog
{
    private static readonly Action<ILogger, int, int, int, long, long, string, Exception?> RunCompleteMessage =
        LoggerMessage.Define<int, int, int, long, long, string>(
            LogLevel.Information,
            new EventId(1, "TickRunComplete"),
            "Tick run complete. Buckets={Buckets} Advanced={Advanced} Skipped={Skipped} DirectTicks={Direct} CompressedTicks={Compressed} Watermark={Watermark}");

    private static readonly Action<ILogger, int, Exception?> ModelCallsMessage =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(3, "TickRunModelCalls"),
            "Worldmind calls in this run: {ModelCalls}.");

    private static readonly Action<ILogger, Exception?> RunFailedMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, "TickRunFailed"), "Tick run failed.");

    public static void RunComplete(
        ILogger logger,
        int buckets,
        int advanced,
        int skipped,
        long direct,
        long compressed,
        int modelCalls,
        string watermark)
    {
        RunCompleteMessage(logger, buckets, advanced, skipped, direct, compressed, watermark, null);
        ModelCallsMessage(logger, modelCalls, null);
    }

    public static void RunFailed(ILogger logger, Exception exception) => RunFailedMessage(logger, exception);
}
