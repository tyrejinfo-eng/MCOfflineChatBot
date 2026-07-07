using System.Net;
using System.Net.Sockets;
using System.Security;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Shared.Security;

/// <summary>
/// v1.1.71: Interface for SSRF-safe HTTP operations. Enables DI and unit testing.
/// </summary>
public interface ISafeHttpClient : IDisposable
{
    Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct = default);
    Task<HttpResponseMessage> GetAsync(Uri requestUri, CancellationToken ct = default);
    Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content, CancellationToken ct = default);
    Task<HttpResponseMessage> PostAsync(Uri requestUri, HttpContent? content, CancellationToken ct = default);
    Task<string> GetStringAsync(string requestUri, CancellationToken ct = default);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default);
}

/// <summary>
/// SSRF-safe HTTP client that blocks requests to private/internal IP ranges
/// and enforces HTTPS for external services. Inherits from HttpClient so it
/// can be used as a drop-in replacement anywhere HttpClient is used.
///
/// All requests flow through the overridden SendAsync, which validates URLs
/// (scheme, embedded credentials). The SocketsHttpHandler ConnectCallback
/// validates resolved IP addresses before connecting, preventing DNS rebinding.
///
/// For services that MUST connect to localhost (e.g., SD WebUI, TTS server),
/// pass allowLocalhost: true to skip the private IP check.
/// </summary>
public class SafeHttpClient : HttpClient, ISafeHttpClient
{
    private readonly bool _requireHttps;

    private readonly bool _allowLocalhost;

    /// <summary>
    /// Creates an SSRF-safe HttpClient.
    /// </summary>
    /// <param name="requireHttps">If true, rejects non-HTTPS URLs.</param>
    /// <param name="timeout">Request timeout. Default: 30 seconds.</param>
    /// <param name="allowLocalhost">If true, allows connections to private/loopback IPs (for localhost services).</param>
    public SafeHttpClient(bool requireHttps = false, TimeSpan? timeout = null, bool allowLocalhost = false)
        : base(CreateHandler(allowLocalhost), disposeHandler: true)
    {
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
        _requireHttps = requireHttps;
        _allowLocalhost = allowLocalhost;
    }

    /// <summary>
    /// Overrides SendAsync to validate URLs and perform pre-flight DNS rebinding
    /// checks before sending requests. All HttpClient methods (GetAsync, PostAsync,
    /// GetStringAsync, PostAsJsonAsync, etc.) flow through SendAsync, so this single
    /// override protects all call paths.
    /// </summary>
    public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        // v1.1.77: Pre-flight DNS rebinding protection — resolve the hostname and
        // verify the resolved IP is not private/loopback for non-private hostnames.
        // This complements the ConnectCallback check with an early-reject + logging layer.
        if (!_allowLocalhost && request.RequestUri is { } uri && !IsExplicitLocalhost(uri.Host))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
                foreach (var addr in addresses)
                {
                    if (IsPrivateOrRestricted(addr))
                    {
                        SglLogger.Warning("DNS rebinding blocked: {Host} resolved to private IP {IP}. Request to {Url} denied.",
                            uri.Host, addr, uri.AbsoluteUri);
                        throw new SecurityException(
                            $"DNS rebinding blocked: hostname '{uri.Host}' resolved to private/restricted IP {addr}");
                    }
                }
            }
            catch (SecurityException)
            {
                throw; // Re-throw our own SecurityException
            }
            catch (Exception ex)
            {
                // DNS resolution failure — log and let the base handler deal with it
                SglLogger.Warning("DNS pre-flight check failed for {Host}: {Error}", uri.Host, ex.Message);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Overrides Send to validate URLs before sending requests (synchronous path).
    /// </summary>
    public override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        return base.Send(request, cancellationToken);
    }

    private void ValidateRequest(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri == null) return;

        if (_requireHttps && uri.Scheme != Uri.UriSchemeHttps)
            throw new SecurityException($"SSRF: HTTPS required but got {uri.Scheme}");

        // Block dangerous schemes
        if (uri.Scheme is "file" or "ftp" or "gopher" or "ldap")
            throw new SecurityException($"SSRF: Blocked scheme {uri.Scheme}");

        // Block embedded credentials
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new SecurityException("SSRF: URLs with embedded credentials are blocked");
    }

    /// <summary>
    /// Creates a SocketsHttpHandler with a ConnectCallback that validates resolved
    /// IP addresses before establishing the TCP connection — prevents DNS rebinding.
    /// </summary>
    private static SocketsHttpHandler CreateHandler(bool allowLocalhost)
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, ct) =>
            {
                // Resolve DNS and validate IPs BEFORE connecting
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);

                if (!allowLocalhost)
                {
                    foreach (var addr in addresses)
                    {
                        if (IsPrivateOrRestricted(addr))
                            throw new SecurityException($"SSRF blocked: connection to private/restricted IP {addr} via host {context.DnsEndPoint.Host}");
                    }
                }

                // Connect to the first valid address
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    /// <summary>
    /// Checks if an IP address is in a private, loopback, link-local, or restricted range.
    /// </summary>
    public static bool IsPrivateOrRestricted(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            // ::1 (loopback) - already handled by IPAddress.IsLoopback
            // fe80::/10 (link-local)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
            // fc00::/7 (unique local)
            if ((bytes[0] & 0xFE) == 0xFC) return true;
            // fec0::/10 (deprecated site-local)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0) return true;
            // ::ffff:0:0/96 (IPv4-mapped) - check the embedded IPv4
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 &&
                bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0 &&
                bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0xFF && bytes[11] == 0xFF)
            {
                var ipv4 = new IPAddress(new[] { bytes[12], bytes[13], bytes[14], bytes[15] });
                return IsPrivateOrRestricted(ipv4);
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the hostname is an explicit localhost/loopback reference
    /// (not a public hostname that might rebind to localhost via DNS).
    /// </summary>
    private static bool IsExplicitLocalhost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal)
            || string.Equals(host, "[::1]", StringComparison.Ordinal);
    }
}
