using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MCOfflineChat.Api.Contracts;
using MCOfflineChat.Api.Contracts.Models;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

/// <summary>
/// HTTP client service for communicating with the optional MC Offline Chat sync service
/// through the Cloudflare tunnel at syntheticgamelabs.dpdns.org.
/// </summary>
public class ApiClient : IDisposable
{
    private HttpClient _httpClient;
    private readonly AppPreferences _prefs;
    private CancellationTokenSource? _heartbeatCts;
    private Timer? _heartbeatTimer;
    private bool _disposed;

    public static string AppVersion => MCOfflineChat.Shared.VersionInfo.MobileVersion;
    public const string AppPlatform = "Mobile";

    /// <summary>
    /// Expected SHA-256 certificate thumbprint for production certificate pinning.
    /// Set to the server's TLS certificate thumbprint (uppercase hex, no separators).
    /// When empty, certificate pinning is disabled and standard CA validation is used.
    /// </summary>
    public static string ExpectedCertificateThumbprint { get; set; } = "";

    public event EventHandler<string>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_prefs.AuthToken);
    public string BaseUrl => _prefs.ServerUrl;

    public ApiClient(AppPreferences prefs)
    {
        _prefs = prefs;
        _httpClient = CreateHttpClient();
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = ValidateServerCertificate
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_prefs.ServerUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(_prefs.AuthToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(ApiConstants.AuthScheme, _prefs.AuthToken);
        }

        return client;
    }

    /// <summary>
    /// Refreshes the HttpClient when the server URL changes.
    /// </summary>
    public void RefreshClient()
    {
        _httpClient.Dispose();
        _httpClient = CreateHttpClient();
    }

    /// <summary>
    /// Register a new client with the server.
    /// </summary>
    public async Task<(bool Success, string Message)> RegisterAsync(
        string username, string password, string machineName,
        string platform, string deviceModel, string osVersion)
    {
        try
        {
            var request = new ClientRegistrationRequest
            {
                Username = username,
                Password = password,
                MachineName = machineName,
                ClientVersion = $"Mobile V{AppVersion}",
                Platform = platform,
                DeviceModel = deviceModel,
                OsVersion = osVersion
            };

            var response = await _httpClient.PostAsJsonAsync(
                ApiConstants.ClientRegister, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>();
                if (result != null)
                {
                    _prefs.AuthToken = result.Token;
                    _prefs.ClientId = result.ClientId.ToString();
                    _prefs.Username = username;

                    // Update auth header
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue(ApiConstants.AuthScheme, result.Token);

                    ConnectionStatusChanged?.Invoke(this, "Connected");
                    return (true, $"Registered successfully. Server: {result.ServerVersion}");
                }
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Registration failed: {error}");
        }
        catch (HttpRequestException ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return (false, $"Connection error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Request timed out. Check your server URL and network.");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Log in an existing client.
    /// </summary>
    public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
    {
        try
        {
            var request = new ClientLoginRequest
            {
                Username = username,
                Password = password
            };

            var response = await _httpClient.PostAsJsonAsync(
                ApiConstants.ClientLogin, request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>();
                if (result != null)
                {
                    _prefs.AuthToken = result.Token;
                    _prefs.ClientId = result.ClientId.ToString();
                    _prefs.Username = username;

                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue(ApiConstants.AuthScheme, result.Token);

                    ConnectionStatusChanged?.Invoke(this, "Connected");
                    return (true, "Login successful");
                }
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Login failed: {error}");
        }
        catch (HttpRequestException ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return (false, $"Connection error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Request timed out. Check your server URL and network.");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get server status information.
    /// </summary>
    public async Task<ServerStatusResponse?> GetServerStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(ApiConstants.ServerStatus);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ServerStatusResponse>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Send a heartbeat to the server with current client statistics.
    /// </summary>
    public async Task<ClientHeartbeatResponse?> SendHeartbeatAsync(
        bool protectionActive, int threatsDetected, int filesScanned, double uptimeMinutes)
    {
        try
        {
            if (!Guid.TryParse(_prefs.ClientId, out var clientId))
                return null;

            var request = new ClientHeartbeatRequest
            {
                ClientId = clientId,
                RealTimeProtectionActive = protectionActive,
                ThreatsDetected = threatsDetected,
                FilesScanned = filesScanned,
                UptimeMinutes = uptimeMinutes,
                SignatureVersion = "0.0.0",
                LlmModelLoaded = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                ApiConstants.ClientHeartbeat, request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ClientHeartbeatResponse>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Check for and download signature updates from the server.
    /// </summary>
    public async Task<SignatureUpdateResponse?> DownloadSignaturesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(ApiConstants.SignatureCheck);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<SignatureUpdateResponse>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Send a chat message to the server-hosted LLM and get a response.
    /// </summary>
    public async Task<string> SendChatMessageAsync(string message, string model = "", string? systemPrompt = null)
    {
        try
        {
            var payload = new
            {
                message = message,
                model = string.IsNullOrEmpty(model) ? _prefs.SelectedLlmModel : model,
                systemPrompt = systemPrompt
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/chat", payload);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("response", out var responseText))
                {
                    return responseText.GetString() ?? "No response from server.";
                }
            }

            return "Failed to get response from server.";
        }
        catch (HttpRequestException)
        {
            return "Connection error. Make sure the server is accessible.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Request TTS audio from the server for the given text.
    /// Uses a 15-second per-request timeout to avoid hanging the UI.
    /// </summary>
    public async Task<Stream?> GetTtsAudioAsync(string text, string? voice = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); // 15s timeout for TTS
            var payload = new { text = text, voice = voice ?? "Chelsie" };
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/tts/speak", payload, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // TTS request timed out — let caller fall back to native TTS silently
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Get available LLM models from the server with full metadata.
    /// </summary>
    public async Task<List<Models.LlmModelListItem>> GetAvailableModelsDetailedAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/llm/models");

            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var models = await response.Content.ReadFromJsonAsync<List<Models.LlmModelListItem>>(options);
                return models ?? new List<Models.LlmModelListItem>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return new List<Models.LlmModelListItem>();
    }

    /// <summary>
    /// Get available LLM model names from the server (legacy compatibility).
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync()
    {
        var detailed = await GetAvailableModelsDetailedAsync();
        return detailed
            .Where(m => m.IsAvailableOnServer && !m.IsEmbeddingModel)
            .Select(m => m.Id)
            .ToList();
    }

    /// <summary>
    /// Get the file size of a specific LLM model from the server.
    /// </summary>
    public async Task<long> GetModelSizeAsync(string modelId)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/llm/models/{Uri.EscapeDataString(modelId)}/size");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("bytes", out var bytes))
                    return bytes.GetInt64();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Could not read model size: {ex.Message}");
            SglLogger.Warning("[ApiClient] GetModelSizeAsync failed: {0}", ex.Message);
        }

        return 0;
    }

    /// <summary>
    /// Start periodic heartbeat to the server.
    /// </summary>
    public void StartHeartbeat(TimeSpan interval)
    {
        StopHeartbeat();
        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTimer = new Timer(async _ =>
        {
            if (_heartbeatCts?.IsCancellationRequested == true) return;
            await SendHeartbeatAsync(true, _prefs.ThreatsBlocked, 0, 0);
        }, null, TimeSpan.Zero, interval);
    }

    /// <summary>
    /// Stop the periodic heartbeat.
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _heartbeatCts = null;
    }

    /// <summary>
    /// Test connectivity to the server.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var status = await GetServerStatusAsync();
            if (status != null)
            {
                ConnectionStatusChanged?.Invoke(this, "Connected");
                return true;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusChanged?.Invoke(this, $"Connection test failed: {ex.Message}");
            SglLogger.Warning("[ApiClient] TestConnectionAsync failed: {0}", ex.Message);
        }

        ConnectionStatusChanged?.Invoke(this, "Disconnected");
        return false;
    }

    /// <summary>
    /// Upload threat knowledge JSON to the server for learning.
    /// </summary>
    public async Task<bool> PostThreatKnowledgeAsync(string knowledgeJson)
    {
        try
        {
            var content = new StringContent(knowledgeJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                $"{ApiConstants.ApiPrefix}/threats/knowledge", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Download an LLM model stream from the server.
    /// Uses a separate HttpClient with no timeout for large file downloads.
    /// </summary>
    public async Task<(Stream? Stream, long ContentLength)> DownloadModelStreamWithSizeAsync(string modelId)
    {
        try
        {
            // Use a separate client with no timeout for large downloads
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ValidateServerCertificate
            };
            var downloadClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_prefs.ServerUrl.TrimEnd('/')),
                Timeout = Timeout.InfiniteTimeSpan
            };

            if (!string.IsNullOrEmpty(_prefs.AuthToken))
            {
                downloadClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(ApiConstants.AuthScheme, _prefs.AuthToken);
            }

            var response = await downloadClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/llm/models/{Uri.EscapeDataString(modelId)}/download",
                HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                var contentLength = response.Content.Headers.ContentLength ?? 0;
                var stream = await response.Content.ReadAsStreamAsync();
                return (stream, contentLength);
            }

            downloadClient.Dispose();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }

        return (null, 0);
    }

    /// <summary>
    /// Download an LLM model stream from the server (legacy).
    /// </summary>
    public async Task<Stream?> DownloadModelStreamAsync(string modelName)
    {
        var (stream, _) = await DownloadModelStreamWithSizeAsync(modelName);
        return stream;
    }

    /// <summary>
    /// Get the current admin broadcast message from the server.
    /// </summary>
    public async Task<BroadcastMessage?> GetBroadcastAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiConstants.ApiPrefix}/broadcast");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<BroadcastMessage>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Broadcast request failed: {ex.Message}");
            SglLogger.Warning("[ApiClient] GetBroadcastAsync failed: {0}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Send a broadcast message to all connected clients via the server.
    /// </summary>
    public async Task SendBroadcastAsync(string message, string priority = "info")
    {
        var payload = new { message, priority };
        await _httpClient.PostAsJsonAsync($"{ApiConstants.ApiPrefix}/broadcast", payload);
    }

    /// <summary>
    /// Request image generation from the server's SD WebUI integration.
    /// Returns a base64 image string or download URL.
    /// </summary>
    public async Task<string> PostImageGenerationAsync(string prompt, int steps = 20, int width = 512, int height = 512)
    {
        try
        {
            var payload = new { prompt, negative_prompt = "", steps, width, height, cfg_scale = 7 };
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/image/generate", payload);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (result.TryGetProperty("imageUrl", out var url))
                    return url.GetString() ?? "";
                if (result.TryGetProperty("image", out var b64))
                    return b64.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return string.Empty;
    }

    /// <summary>
    /// Download a generated image as a stream.
    /// </summary>
    public async Task<Stream?> DownloadImageAsync(string imageUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync(imageUrl);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Image download failed: {ex.Message}");
            SglLogger.Warning("[ApiClient] DownloadImageAsync failed: {0}", ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Send a message to a chat room via the proper ChatRoom endpoint.
    /// </summary>
    public async Task<bool> SendChatRoomMessageAsync(string room, string message, string? username = null)
    {
        try
        {
            var payload = new { message, username };
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/chatroom/{Uri.EscapeDataString(room)}", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get chat room messages with pagination.
    /// </summary>
    public async Task<JsonElement?> GetChatRoomMessagesAsync(string room, int skip = 0, int take = 50)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/chatroom/{Uri.EscapeDataString(room)}?skip={skip}&take={take}");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Chat room messages failed: {ex.Message}");
            SglLogger.Warning("[ApiClient] GetChatRoomMessagesAsync failed: {0}", ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Shared SSL certificate validation callback used by all HttpClient instances.
    /// - Accepts valid certificates from trusted CAs.
    /// - For production: enforces certificate pinning when ExpectedCertificateThumbprint is set.
    /// - For development: allows self-signed certs on localhost and private network IPs.
    /// - Rejects all other invalid certificates.
    /// </summary>
    private static bool ValidateServerCertificate(
        HttpRequestMessage message, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        var host = message?.RequestUri?.Host;

        // Allow self-signed certs ONLY for loopback (localhost/127.0.0.1).
        // LAN IPs (192.168.x, 10.x) must use valid certs to prevent MITM on local networks.
        bool isLoopback = host != null && (host == "localhost" || host == "127.0.0.1");

        if (isLoopback)
            return true;

        // For remote hosts, reject if there are any SSL policy errors
        if (errors != SslPolicyErrors.None)
            return false;

        // Certificate is valid per CA chain — enforce pinning if a thumbprint is configured
        if (!string.IsNullOrEmpty(ExpectedCertificateThumbprint) && cert != null)
        {
            var actualThumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
            if (!string.Equals(actualThumbprint, ExpectedCertificateThumbprint, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    // ═══ v1.1.53 Security Engine API Methods ═══

    /// <summary>
    /// Get recent security alerts from the server's AlertEngine.
    /// </summary>
    public async Task<List<JsonElement>> GetAlertsAsync(int count = 20)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/alerts?count={count}");
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<List<JsonElement>>(options)
                    ?? new List<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// Get the detection pipeline status from the server's DetectionEngine.
    /// </summary>
    public async Task<JsonElement?> GetDetectionStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/detection/status");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Get response actions audit log from the server's ResponseEngine.
    /// </summary>
    public async Task<List<JsonElement>> GetResponseActionsAsync(int count = 20)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/response/actions?count={count}");
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<List<JsonElement>>(options)
                    ?? new List<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// Get all engine statuses from the server's EngineOrchestrator.
    /// </summary>
    public async Task<List<JsonElement>> GetEngineStatusesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/engines");
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<List<JsonElement>>(options)
                    ?? new List<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// Submit device telemetry events to the server's IngestionGateway.
    /// </summary>
    public async Task<bool> PostTelemetryEventsAsync(object[] events)
    {
        try
        {
            var payload = new { events };
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/ingest", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    // ═══ Swarm Storage API Methods ═══

    /// <summary>
    /// Get swarm network status from GET /api/v1/swarm/status.
    /// Returns null if the request fails.
    /// </summary>
    public async Task<JsonElement?> GetSwarmStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiConstants.ApiPrefix}/swarm/status");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return null;
    }

    /// <summary>
    /// Get recent swarm signals from GET /api/v1/swarm/signals.
    /// Returns an empty list on failure.
    /// </summary>
    public async Task<List<JsonElement>> GetSwarmSignalsAsync(int count = 50)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/swarm/signals?count={count}");
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<List<JsonElement>>(options)
                    ?? new List<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// Upload a threat intelligence signal via POST /api/v1/swarm/signal.
    /// Returns true if the server accepted the payload.
    /// </summary>
    public async Task<bool> PostSwarmSignalAsync(object signal)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/swarm/signal", signal);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Trigger a full swarm synchronisation via POST /api/v1/swarm/sync.
    /// Returns true if the server accepted the request.
    /// </summary>
    public async Task<bool> TriggerSwarmSyncAsync()
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiConstants.ApiPrefix}/swarm/sync", new { });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    // ═══ Swarm Storage File API Methods ═══

    /// <summary>
    /// List files stored in swarm storage via GET /api/v1/swarm/storage/files.
    /// Returns an empty list on failure. JWT is sent automatically via the Authorization header.
    /// </summary>
    public async Task<List<JsonElement>> GetSwarmStorageFilesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/swarm/storage/files");
            if (response.IsSuccessStatusCode)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<List<JsonElement>>(options)
                    ?? new List<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// Download a swarm storage file by ID via GET /api/v1/swarm/storage/files/{fileId}/download.
    /// Returns null if the download fails. Caller is responsible for disposing the stream.
    /// </summary>
    public async Task<(Stream? Stream, string FileName)> DownloadSwarmFileAsync(string fileId, string fallbackName)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/swarm/storage/files/{Uri.EscapeDataString(fileId)}/download",
                HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                // Try to extract the server-suggested filename from Content-Disposition
                var cd = response.Content.Headers.ContentDisposition;
                var serverName = cd?.FileNameStar ?? cd?.FileName ?? fallbackName;
                var stream = await response.Content.ReadAsStreamAsync();
                return (stream, serverName.Trim('"'));
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        return (null, fallbackName);
    }

    /// <summary>
    /// Upload a file to swarm storage via POST /api/v1/swarm-storage/upload (multipart form).
    /// Returns true if the server accepted the upload.
    /// </summary>
    public async Task<bool> UploadSwarmFileAsync(string fileName, byte[] fileData, bool isPublic = false, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(fileData), "file", fileName);
            content.Add(new StringContent(isPublic.ToString().ToLower()), "isPublic");

            var response = await _httpClient.PostAsync(
                $"{ApiConstants.ApiPrefix}/swarm-storage/upload", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Delete a swarm storage file by ID via DELETE /api/v1/swarm-storage/files/{fileId}.
    /// Returns true if the server accepted the deletion.
    /// </summary>
    public async Task<bool> DeleteSwarmFileAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{ApiConstants.ApiPrefix}/swarm-storage/files/{Uri.EscapeDataString(fileId)}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Retrieve swarm storage quota information via GET /api/v1/swarm-storage/quota.
    /// Returns null if the request fails or the server returns a non-success status.
    /// </summary>
    public async Task<SwarmQuotaInfo?> GetSwarmStorageQuotaAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{ApiConstants.ApiPrefix}/swarm-storage/quota", ct);
            if (!response.IsSuccessStatusCode) return null;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return await response.Content.ReadFromJsonAsync<SwarmQuotaInfo>(options, ct);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopHeartbeat();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
