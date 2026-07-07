using Serilog;
using Serilog.Events;

namespace MCOfflineChat.Shared.Logging;

public static class SglLogger
{
    private static bool _initialized;
    private static string? _logDirectory;
    private static string? _logFilePattern;
    private const long MaxLogFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const int MaxRotatedFiles = 10;
    private static long _lastRotationCheckTicks;
    private static readonly long RotationCheckIntervalTicks = TimeSpan.FromMinutes(5).Ticks;
    private static readonly Dictionary<string, ILogger> _componentLoggers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ComponentNames = ["server", "eventbus", "llm", "detection", "swarm", "auth"];

    public static void Initialize(string logDirectory)
    {
        if (_initialized)
            return;

        Directory.CreateDirectory(logDirectory);
        _logDirectory = logDirectory;
        _logFilePattern = Path.Combine(logDirectory, "sgl-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                _logFilePattern,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: MaxLogFileSizeBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: MaxRotatedFiles,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Create component-specific sub-loggers that write to separate files
        foreach (var component in ComponentNames)
        {
            var componentPattern = Path.Combine(logDirectory, $"{component}-.log");
            var componentLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    componentPattern,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: MaxLogFileSizeBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: MaxRotatedFiles,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            _componentLoggers[component] = componentLogger;
        }

        _initialized = true;
    }

    /// <summary>
    /// Checks if manual log rotation is needed and cleans up old rotated files.
    /// Serilog handles automatic rotation via rollOnFileSizeLimit, but this method
    /// can be called to force cleanup of any stale rotated files beyond the limit.
    /// When called from log methods, checks are throttled to every 5 minutes.
    /// </summary>
    public static void RotateIfNeeded()
    {
        if (_logDirectory == null) return;

        // Throttle: only check every 5 minutes to avoid filesystem overhead
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastRotationCheckTicks);
        if (now - last < RotationCheckIntervalTicks) return;
        if (Interlocked.CompareExchange(ref _lastRotationCheckTicks, now, last) != last) return;

        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "sgl-*.log")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToArray();

            // Delete oldest files beyond the retention limit
            if (logFiles.Length > MaxRotatedFiles)
            {
                foreach (var oldFile in logFiles.Skip(MaxRotatedFiles))
                {
                    try { File.Delete(oldFile); }
                    catch { /* file may be locked */ }
                }
            }
        }
        catch
        {
            // Don't let rotation failures break logging
        }
    }

    public static void Information(string message, params object[] args)
    {
        RotateIfNeeded();
        Log.Information(LogRedactor.Redact(message), args);
    }

    public static void Warning(string message, params object[] args)
    {
        RotateIfNeeded();
        Log.Warning(LogRedactor.Redact(message), args);
    }

    public static void Error(string message, Exception? ex = null, params object[] args)
    {
        RotateIfNeeded();
        if (ex != null)
            Log.Error(ex, LogRedactor.Redact(message), args);
        else
            Log.Error(LogRedactor.Redact(message), args);
    }

    public static void Debug(string message, params object[] args)
    {
        RotateIfNeeded();
        Log.Debug(LogRedactor.Redact(message), args);
    }

    /// <summary>
    /// Writes a log entry to both the main logger and the component-specific logger.
    /// If the component logger does not exist, falls back to the main logger only.
    /// </summary>
    public static void LogComponent(string component, LogEventLevel level, string message, params object[] args)
    {
        RotateIfNeeded();
        var redacted = LogRedactor.Redact(message);

        // Always write to the main combined log
        Log.Write(level, redacted, args);

        // Also write to the component-specific log if it exists
        if (_componentLoggers.TryGetValue(component, out var componentLogger))
        {
            componentLogger.Write(level, redacted, args);
        }
    }

    /// <summary>Logs an Information-level message to the "server" component log and the main log.</summary>
    public static void Server(string message, params object[] args)
        => LogComponent("server", LogEventLevel.Information, message, args);

    /// <summary>Logs an Information-level message to the "eventbus" component log and the main log.</summary>
    public static void EventBusLog(string message, params object[] args)
        => LogComponent("eventbus", LogEventLevel.Information, message, args);

    /// <summary>Logs an Information-level message to the "llm" component log and the main log.</summary>
    public static void Llm(string message, params object[] args)
        => LogComponent("llm", LogEventLevel.Information, message, args);

    /// <summary>Logs an Information-level message to the "detection" component log and the main log.</summary>
    public static void Detection(string message, params object[] args)
        => LogComponent("detection", LogEventLevel.Information, message, args);

    /// <summary>Logs an Information-level message to the "swarm" component log and the main log.</summary>
    public static void Swarm(string message, params object[] args)
        => LogComponent("swarm", LogEventLevel.Information, message, args);

    /// <summary>Logs an Information-level message to the "auth" component log and the main log.</summary>
    public static void Auth(string message, params object[] args)
        => LogComponent("auth", LogEventLevel.Information, message, args);

    public static void CloseAndFlush()
    {
        // Close all component loggers first
        foreach (var kvp in _componentLoggers)
        {
            if (kvp.Value is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { /* don't let component cleanup break main flush */ }
            }
        }
        _componentLoggers.Clear();

        Log.CloseAndFlush();
    }
}
