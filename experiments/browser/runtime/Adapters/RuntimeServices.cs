using Brutal.Logging;
using Microsoft.Extensions.Logging;

namespace BrowserAdapters;

// Browser logging must not instantiate the desktop ConsoleLogger's worker.
// Keep engine errors observable, especially exceptions caught by UnityWorld.
public static class RuntimeServices
{
    public static List<string> Errors { get; } = new();
    public static int ErrorCount { get; private set; }
    public static int OmittedErrorCount { get; private set; }
    private static readonly HashSet<string> reported = new();

    private sealed class Provider : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? error,
            Func<TState, Exception?, string> formatter
        )
        {
            if (IsEnabled(level))
            {
                var message = formatter(state, error);
                if (level >= LogLevel.Error)
                {
                    ErrorCount++;
                    if (reported.Contains(message))
                        return;
                    if (Errors.Count >= 100)
                    {
                        OmittedErrorCount++;
                        return;
                    }
                    reported.Add(message);
                    Errors.Add(message);
                }
                Console.Error.WriteLine(message);
            }
        }
    }

    public static void ConfigureLogging()
    {
        LogSystem.IsEnabled = true;
        LogSystem.Initialize(builder => builder.AddProvider(new Provider()));
    }

    // Serialize through the installed atomic temp-file/rename writer. Match the
    // worker's best-effort IO policy, without a background thread or bounded queue.
    public static void EnqueueCache(string path, byte[] contents)
    {
        try
        {
            Waylith.Runtime.CacheFileWriter.Write(path, contents);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void DesktopSteamUnavailable() =>
        Console.WriteLine("Browser: desktop Steam integration is unavailable; continuing offline");

    public static void TraceLoadingStage(string stage) =>
        Console.WriteLine("Loading stage: " + stage);

    public static void RunStartupConstructor(RuntimeTypeHandle handle)
    {
        TraceLoadingStage("static constructor " + Type.GetTypeFromHandle(handle)?.FullName);
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(handle);
    }

    // Complete an installed GenThreading batch before its caller checks the
    // completion counter. The original batch still owns exceptions and signaling.
    public static bool RunInline(WaitCallback callback)
    {
        callback(null);
        return true;
    }

    public static void TraceSubdivision(int index) =>
        TraceLoadingStage("planet subdivision " + index);

    public static void TraceTile(int index)
    {
        if (index % 4096 == 0)
            TraceLoadingStage("world tile " + index);
    }
}
