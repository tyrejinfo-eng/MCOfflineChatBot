using System.IO.Pipes;
using System.Text;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Services;

/// <summary>
/// Ensures only one instance of the MC Offline Chat client runs at a time.
/// Uses a named mutex for detection and named pipes for IPC.
/// When a second instance is launched, it signals the existing instance
/// to bring its window to the foreground, then exits.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Global\\MC Offline Chat_MCOfflineChat_Client";
    private const string PipeName = "MC Offline Chat_MCOfflineChat_IPC";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    /// <summary>
    /// Raised when another instance signals this one to activate.
    /// The UI should bring its main window to the foreground.
    /// </summary>
    public event Action? ActivateRequested;

    /// <summary>
    /// Try to acquire the single-instance mutex.
    /// Returns true if this is the first instance (proceed with startup).
    /// Returns false if another instance is already running (signal it and exit).
    /// </summary>
    public bool TryAcquire()
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SingleInstance] Mutex creation failed: {0}", ex.Message);
            return true; // Allow startup on failure
        }

        if (!createdNew)
        {
            // Another instance is running — signal it
            SignalExistingInstance();
            return false;
        }

        // We are the first instance — start listening for signals
        StartPipeListener();
        return true;
    }

    /// <summary>
    /// Signal the existing instance to bring its window to the foreground.
    /// </summary>
    private static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);

            var buffer = Encoding.UTF8.GetBytes("ACTIVATE");
            client.Write(buffer, 0, buffer.Length);
            client.Flush();

            SglLogger.Information("[SingleInstance] Signaled existing instance to activate");
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[SingleInstance] Could not signal existing instance: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Start background listener for IPC signals from other instances.
    /// </summary>
    private void StartPipeListener()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _listenerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    var buffer = new byte[256];
                    var bytesRead = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    if (message == "ACTIVATE")
                    {
                        SglLogger.Information("[SingleInstance] Received ACTIVATE signal from another instance");
                        ActivateRequested?.Invoke();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SglLogger.Warning("[SingleInstance] Pipe listener error: {0}", ex.Message);
                    await Task.Delay(1000, token);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
