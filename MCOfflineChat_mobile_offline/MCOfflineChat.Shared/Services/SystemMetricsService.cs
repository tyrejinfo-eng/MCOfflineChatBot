using System.Diagnostics;
using System.Text.Json;
using MCOfflineChat.Shared.Logging;
using MCOfflineChat.Shared.Telemetry;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Collects real system metrics (CPU, RAM, disk, threads, LLM status) directly in-process.
/// Publishes metrics to EventBus and writes periodic snapshots to logs/system_metrics.log.
/// This replaces HTTP-based metrics collection to avoid localhost connection refused errors.
/// </summary>
public sealed class SystemMetricsService : IDisposable
{
    private readonly EventBus? _eventBus;
    private readonly Timer _collectionTimer;
    private readonly string _logPath;
    private volatile bool _disposed;

    // Latest snapshot — thread-safe reads
    private volatile MetricsSnapshot _latest = new();

    // Tracking for CPU calculation
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck = DateTime.UtcNow;
    private int _exceptionCount;
    private int _threadCrashCount;
    private readonly object _counterLock = new();

    /// <summary>Current metrics snapshot. Safe to read from any thread.</summary>
    public MetricsSnapshot Latest => _latest;

    /// <summary>Increment exception counter (call from global exception handlers).</summary>
    public void RecordException() => Interlocked.Increment(ref _exceptionCount);

    /// <summary>Increment thread crash counter.</summary>
    public void RecordThreadCrash() => Interlocked.Increment(ref _threadCrashCount);

    // LLM status callback — set by the LLM service layer
    private Func<LlmMetrics>? _llmMetricsProvider;
    public void SetLlmMetricsProvider(Func<LlmMetrics> provider) => _llmMetricsProvider = provider;

    // Server status callback — set by the API server
    private Func<ServerMetrics>? _serverMetricsProvider;
    public void SetServerMetricsProvider(Func<ServerMetrics> provider) => _serverMetricsProvider = provider;

    public SystemMetricsService(EventBus? eventBus = null, int intervalMs = 5000)
    {
        _eventBus = eventBus;
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "system_metrics.log");

        // Collect immediately, then every intervalMs
        Collect();
        _collectionTimer = new Timer(_ => Collect(), null, intervalMs, intervalMs);
    }

    private void Collect()
    {
        if (_disposed) return;

        try
        {
            var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;

            // CPU usage calculation (process CPU time delta / wall time delta)
            var currentCpuTime = process.TotalProcessorTime;
            var cpuElapsed = (now - _lastCpuCheck).TotalMilliseconds;
            double cpuPercent = 0;
            if (cpuElapsed > 0 && _lastCpuCheck != DateTime.MinValue)
            {
                var cpuDelta = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                cpuPercent = Math.Round(cpuDelta / (cpuElapsed * Environment.ProcessorCount) * 100, 1);
                cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            }
            _lastCpuTime = currentCpuTime;
            _lastCpuCheck = now;

            // RAM
            var workingSetMb = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 1);
            var privateMemMb = Math.Round(process.PrivateMemorySize64 / (1024.0 * 1024.0), 1);
            var gcTotalMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 1);

            // System-wide RAM via GC info
            var gcInfo = GC.GetGCMemoryInfo();
            var totalPhysicalMb = Math.Round(gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0), 0);
            var availableRamMb = totalPhysicalMb - workingSetMb; // approximate

            // Threads
            var threadCount = process.Threads.Count;
            var threadPoolInfo = GetThreadPoolInfo();

            // Disk
            var baseDrive = Path.GetPathRoot(AppContext.BaseDirectory);
            long diskFreeMb = 0;
            long diskTotalMb = 0;
            if (!string.IsNullOrEmpty(baseDrive))
            {
                try
                {
                    var driveInfo = new DriveInfo(baseDrive);
                    diskFreeMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
                    diskTotalMb = driveInfo.TotalSize / (1024 * 1024);
                }
                catch { /* drive info may fail */ }
            }

            // LLM metrics
            var llmMetrics = _llmMetricsProvider?.Invoke() ?? new LlmMetrics();

            // Server metrics
            var serverMetrics = _serverMetricsProvider?.Invoke() ?? new ServerMetrics();

            // Errors
            var exceptions = Interlocked.Exchange(ref _exceptionCount, 0);
            var threadCrashes = Interlocked.Exchange(ref _threadCrashCount, 0);

            var snapshot = new MetricsSnapshot
            {
                Timestamp = now,
                CpuPercent = cpuPercent,
                RamWorkingSetMb = workingSetMb,
                RamPrivateMb = privateMemMb,
                GcHeapMb = gcTotalMb,
                TotalPhysicalRamMb = totalPhysicalMb,
                ThreadCount = threadCount,
                ThreadPoolWorkers = threadPoolInfo.workers,
                ThreadPoolIoThreads = threadPoolInfo.io,
                DiskFreeMb = diskFreeMb,
                DiskTotalMb = diskTotalMb,
                Exceptions = exceptions,
                ThreadCrashes = threadCrashes,
                Uptime = now - process.StartTime.ToUniversalTime(),
                LlmStatus = llmMetrics.Status,
                LlmModelName = llmMetrics.ModelName,
                LlmMemoryMb = llmMetrics.MemoryMb,
                LlmModelsLoaded = llmMetrics.ModelsLoaded,
                ServerIsRunning = serverMetrics.IsRunning,
                ServerPort = serverMetrics.Port,
                ServerOnlineClients = serverMetrics.OnlineClients,
                ServerRequestsTotal = serverMetrics.RequestsTotal,
                ServerResponseTimeMs = serverMetrics.AvgResponseTimeMs,
                EnginesRunning = serverMetrics.EnginesRunning,
                EnginesTotalCount = serverMetrics.EnginesTotalCount,
                IocIngested = serverMetrics.IocIngested,
                QueueDepth = serverMetrics.QueueDepth,
            };

            _latest = snapshot;

            // Write to log file (append JSONL)
            WriteMetricsLog(snapshot);

            // Publish to EventBus
            PublishToEventBus(snapshot);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("SystemMetricsService collection failed: {Error}", ex.Message);
        }
    }

    private void WriteMetricsLog(MetricsSnapshot s)
    {
        try
        {
            var record = new
            {
                timestamp = s.Timestamp.ToString("o"),
                cpu = s.CpuPercent,
                ram = s.RamWorkingSetMb,
                ram_private = s.RamPrivateMb,
                gc_heap = s.GcHeapMb,
                threads_alive = s.ThreadCount,
                pool_workers = s.ThreadPoolWorkers,
                pool_io = s.ThreadPoolIoThreads,
                disk_free_mb = s.DiskFreeMb,
                exceptions = s.Exceptions,
                thread_crashes = s.ThreadCrashes,
                uptime_seconds = (int)s.Uptime.TotalSeconds,
                llm_status = s.LlmStatus,
                llm_model = s.LlmModelName,
                llm_memory_mb = s.LlmMemoryMb,
                llm_models_loaded = s.LlmModelsLoaded,
                server_running = s.ServerIsRunning,
                server_port = s.ServerPort,
                online_clients = s.ServerOnlineClients,
                requests_total = s.ServerRequestsTotal,
                response_time_ms = s.ServerResponseTimeMs,
                engines_running = s.EnginesRunning,
                engines_total = s.EnginesTotalCount,
                ioc_ingested = s.IocIngested,
                queue_depth = s.QueueDepth,
            };

            var json = JsonSerializer.Serialize(record);
            File.AppendAllText(_logPath, json + "\n");

            // Rotate if file exceeds 50MB
            try
            {
                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > 50 * 1024 * 1024)
                {
                    var rotated = _logPath + $".{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
                    File.Move(_logPath, rotated);
                }
            }
            catch { /* rotation best-effort */ }
        }
        catch { /* log write best-effort */ }
    }

    private void PublishToEventBus(MetricsSnapshot s)
    {
        if (_eventBus == null) return;
        try
        {
            var evt = new TelemetryEvent
            {
                EventType = EventTopics.Telemetry.MetricsSnapshot,
                Source = "SystemMetricsService",
                Severity = "Info",
                Priority = EventPriority.Low,
                Data = new Dictionary<string, object>
                {
                    ["cpu"] = s.CpuPercent,
                    ["ram_mb"] = s.RamWorkingSetMb,
                    ["gc_heap_mb"] = s.GcHeapMb,
                    ["threads"] = s.ThreadCount,
                    ["disk_free_mb"] = s.DiskFreeMb,
                    ["exceptions"] = s.Exceptions,
                    ["llm_status"] = s.LlmStatus,
                    ["llm_memory_mb"] = s.LlmMemoryMb,
                    ["engines_running"] = s.EnginesRunning,
                    ["uptime_s"] = (int)s.Uptime.TotalSeconds,
                    ["server_running"] = s.ServerIsRunning,
                    ["online_clients"] = s.ServerOnlineClients,
                    ["ioc_ingested"] = s.IocIngested,
                    ["queue_depth"] = s.QueueDepth,
                }
            };
            _eventBus.Publish(evt);
        }
        catch { /* best-effort */ }
    }

    private static (int workers, int io) GetThreadPoolInfo()
    {
        ThreadPool.GetAvailableThreads(out int workerAvail, out int ioAvail);
        ThreadPool.GetMaxThreads(out int workerMax, out int ioMax);
        return (workerMax - workerAvail, ioMax - ioAvail);
    }

    public void Dispose()
    {
        _disposed = true;
        _collectionTimer.Dispose();
    }
}

/// <summary>Point-in-time metrics snapshot. Immutable once constructed.</summary>
public sealed class MetricsSnapshot
{
    public DateTime Timestamp { get; init; }
    public double CpuPercent { get; init; }
    public double RamWorkingSetMb { get; init; }
    public double RamPrivateMb { get; init; }
    public double GcHeapMb { get; init; }
    public double TotalPhysicalRamMb { get; init; }
    public int ThreadCount { get; init; }
    public int ThreadPoolWorkers { get; init; }
    public int ThreadPoolIoThreads { get; init; }
    public long DiskFreeMb { get; init; }
    public long DiskTotalMb { get; init; }
    public int Exceptions { get; init; }
    public int ThreadCrashes { get; init; }
    public TimeSpan Uptime { get; init; }
    public string LlmStatus { get; init; } = "Idle";
    public string LlmModelName { get; init; } = "";
    public double LlmMemoryMb { get; init; }
    public int LlmModelsLoaded { get; init; }
    public bool ServerIsRunning { get; init; }
    public int ServerPort { get; init; }
    public int ServerOnlineClients { get; init; }
    public long ServerRequestsTotal { get; init; }
    public double ServerResponseTimeMs { get; init; }
    public int EnginesRunning { get; init; }
    public int EnginesTotalCount { get; init; }
    public int IocIngested { get; init; }
    public int QueueDepth { get; init; }
}

/// <summary>LLM status info provided by the LLM layer.</summary>
public sealed class LlmMetrics
{
    public string Status { get; init; } = "Idle";
    public string ModelName { get; init; } = "";
    public double MemoryMb { get; init; }
    public int ModelsLoaded { get; init; }
}

/// <summary>Server status info provided by the API server layer.</summary>
public sealed class ServerMetrics
{
    public bool IsRunning { get; init; }
    public int Port { get; init; }
    public int OnlineClients { get; init; }
    public long RequestsTotal { get; init; }
    public double AvgResponseTimeMs { get; init; }
    public int EnginesRunning { get; init; }
    public int EnginesTotalCount { get; init; }
    public int IocIngested { get; init; }
    public int QueueDepth { get; init; }
}
