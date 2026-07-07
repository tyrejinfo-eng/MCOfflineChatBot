using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Telemetry;

/// <summary>
/// Common Event Format (CEF) exporter for SIEM integration.
/// Subscribes to EventBus and forwards events to external SIEM via syslog UDP/TCP.
/// </summary>
public sealed class SiemExporter : IDisposable
{
    private readonly EventBus _eventBus;
    private UdpClient? _udpClient;
    private TcpClient? _tcpClient;
    private NetworkStream? _tcpStream;
    private bool _enabled;
    private string _host = "127.0.0.1";
    private int _port = 514;
    private string _protocol = "udp"; // udp or tcp
    private long _exportedCount;
    private long _failedCount;

    public long ExportedCount => Interlocked.Read(ref _exportedCount);
    public long FailedCount => Interlocked.Read(ref _failedCount);
    public bool IsEnabled => _enabled;

    public SiemExporter(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void Configure(string host, int port, string protocol = "udp")
    {
        _host = host;
        _port = port;
        _protocol = protocol.ToLowerInvariant();
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;

        try
        {
            if (_protocol == "tcp")
            {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(_host, _port);
                _tcpStream = _tcpClient.GetStream();
            }
            else
            {
                _udpClient = new UdpClient();
                _udpClient.Connect(_host, _port);
            }

            _eventBus.Subscribe("*", OnEvent);
            SglLogger.Information("[SiemExporter] Enabled. Exporting to {Protocol}://{Host}:{Port}", _protocol, _host, _port);
        }
        catch (Exception ex)
        {
            _enabled = false;
            SglLogger.Error("[SiemExporter] Failed to connect", ex);
        }
    }

    public void Disable()
    {
        _enabled = false;
        _tcpStream?.Dispose();
        _tcpClient?.Dispose();
        _udpClient?.Dispose();
        _tcpStream = null;
        _tcpClient = null;
        _udpClient = null;
        SglLogger.Information("[SiemExporter] Disabled");
    }

    private Task OnEvent(TelemetryEvent evt)
    {
        if (!_enabled) return Task.CompletedTask;

        try
        {
            var cef = ToCef(evt);
            var bytes = Encoding.UTF8.GetBytes(cef + "\n");

            if (_protocol == "tcp" && _tcpStream != null)
            {
                _tcpStream.Write(bytes, 0, bytes.Length);
            }
            else if (_udpClient != null)
            {
                _udpClient.Send(bytes, bytes.Length);
            }

            Interlocked.Increment(ref _exportedCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedCount);
            if (Interlocked.Read(ref _failedCount) % 100 == 1)
                SglLogger.Warning("[SiemExporter] Export failed (count={Count}): {Message}", FailedCount, ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>Convert TelemetryEvent to Common Event Format (CEF).</summary>
    private static string ToCef(TelemetryEvent evt)
    {
        var severity = evt.Severity switch
        {
            "Critical" => 10,
            "High" => 8,
            "Warning" => 5,
            "Info" => 3,
            "Low" => 1,
            _ => 3
        };

        var extension = $"src={evt.Source} " +
                        $"rt={evt.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} " +
                        $"msg={EscapeCef(evt.EventType)}";

        if (!string.IsNullOrEmpty(evt.HostId)) extension += $" dhost={EscapeCef(evt.HostId)}";
        if (!string.IsNullOrEmpty(evt.RemoteAddress)) extension += $" dst={evt.RemoteAddress}";
        if (!string.IsNullOrEmpty(evt.CorrelationId)) extension += $" externalId={evt.CorrelationId}";

        return $"CEF:0|MCOfflineChat|MC Offline Chat|{VersionInfo.ServerVersion}|{EscapeCef(evt.EventType)}|{EscapeCef(evt.EventType)}|{severity}|{extension}";
    }

    private static string EscapeCef(string? value) =>
        value?.Replace("\\", "\\\\").Replace("|", "\\|").Replace("=", "\\=").Replace("\n", " ") ?? "";

    public void Dispose()
    {
        Disable();
    }
}
