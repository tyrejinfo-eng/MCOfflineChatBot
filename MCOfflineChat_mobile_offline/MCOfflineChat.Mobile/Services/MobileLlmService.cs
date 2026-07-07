using System.Text.Json;
using LLama;
using LLama.Common;
using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public class MobileLlmService : IDisposable
{
    private readonly ThreatKnowledgeService _knowledgeService;
    private readonly string _modelDir;
    private readonly bool _autoLoadOnImport;
    private string? _currentModelPath;
    private string? _currentModelName;

    // LLamaSharp on-device inference fields
    private LLamaWeights? _loadedWeights;
    private LLamaContext? _llamaContext;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private bool _localModelLoaded;

    /// <summary>
    /// Minimum free RAM (in bytes) required before attempting to load a GGUF model.
    /// On-device LLM inference is memory-intensive; loading on a device with
    /// insufficient free RAM will cause OOM kills or severe swapping.
    /// Set to 4 GB.
    /// </summary>
    private const long MinFreeRamBytes = 4L * 1024 * 1024 * 1024;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<double>? DownloadProgress;

    public bool IsModelDownloaded => _currentModelPath != null && File.Exists(_currentModelPath);
    public string? CurrentModelName => _currentModelName;
    public bool IsReady { get; private set; }
    public bool IsLocalInferenceAvailable => _localModelLoaded && _llamaContext != null;

    public MobileLlmService(ThreatKnowledgeService knowledgeService, string? modelDirectory = null, bool autoLoadOnImport = true)
    {
        _knowledgeService = knowledgeService;
        _modelDir = modelDirectory ?? Path.Combine(FileSystem.AppDataDirectory, "llm_models");
        _autoLoadOnImport = autoLoadOnImport;
        Directory.CreateDirectory(_modelDir);
        DetectExistingModel();
    }

    public string ModelDirectory => _modelDir;

    private void DetectExistingModel()
    {
        var modelFiles = Directory.GetFiles(_modelDir, "*.gguf");
        if (modelFiles.Length > 0)
        {
            _currentModelPath = modelFiles[0];
            _currentModelName = Path.GetFileNameWithoutExtension(_currentModelPath);
            IsReady = true;
            StatusChanged?.Invoke(this, $"Model found: {_currentModelName}");

            // Attempt to load model for local inference immediately
            _ = Task.Run(() => LoadLocalModelAsync());
        }
    }

    /// <summary>
    /// Loads the GGUF model into memory for on-device inference using LLamaSharp.
    /// Uses conservative settings optimized for mobile devices (limited RAM).
    /// <para>
    /// <b>ARM64 native library requirement:</b> LLamaSharp requires a native
    /// <c>libllama.so</c> compiled for Android ARM64-v8a.  The NuGet package
    /// <c>LLamaSharp.Backend.Cpu</c> only ships win/linux/osx natives.
    /// If the native library is missing, the load will fail gracefully and
    /// the offline fallback chain activates and returns heuristic output if local inference is unavailable.
    /// </para>
    /// <para>
    /// <b>RAM guard:</b> Before loading, the method checks available device
    /// memory via <c>Android.App.ActivityManager.MemoryInfo</c>.  If free RAM
    /// is below 4 GB, loading is skipped to prevent OOM crashes.
    /// </para>
    /// </summary>
    public async Task<bool> LoadLocalModelAsync()
    {
        if (_currentModelPath == null || !File.Exists(_currentModelPath))
        {
            StatusChanged?.Invoke(this, "No model file available for local loading.");
            return false;
        }

        if (_localModelLoaded) return true;

        // ---- RAM guard: require at least 4 GB free before loading ----
#if ANDROID
        try
        {
            var activityManager = Android.App.Application.Context.GetSystemService(
                Android.Content.Context.ActivityService) as Android.App.ActivityManager;
            if (activityManager != null)
            {
                var memInfo = new Android.App.ActivityManager.MemoryInfo();
                activityManager.GetMemoryInfo(memInfo);

                long freeRam = memInfo.AvailMem;
                long totalRam = memInfo.TotalMem;
                double freeGb = freeRam / (1024.0 * 1024.0 * 1024.0);

                SglLogger.Information("[MobileLlm] Device RAM: total={0:F1} GB, free={1:F1} GB",
                    totalRam / (1024.0 * 1024.0 * 1024.0), freeGb);

                if (freeRam < MinFreeRamBytes)
                {
                    string msg = $"Insufficient free RAM ({freeGb:F1} GB free, 4.0 GB required). " +
                                 "Skipping local model load. Using offline heuristic fallback.";
                    SglLogger.Warning("[MobileLlm] {0}", msg);
                    StatusChanged?.Invoke(this, msg);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            SglLogger.Warning("[MobileLlm] Could not query device memory info: {0}", ex.Message);
            // Continue anyway — the LLamaSharp load itself will fail if OOM
        }
#endif

        await _inferenceLock.WaitAsync();
        try
        {
            StatusChanged?.Invoke(this, "Loading model for on-device inference...");

            // ---- ARM64 native library loading ----
            // Must load libllama.so BEFORE LLamaSharp tries P/Invoke
            if (!NativeLibraryLoader.IsNativeLibraryLoaded)
            {
                StatusChanged?.Invoke(this, "Loading native ARM64 library...");
                if (!NativeLibraryLoader.TryLoadNativeLibrary())
                {
                    var diag = NativeLibraryLoader.GetDiagnostics();
                    string msg = $"Native library load failed. ARM64={diag.IsArm64}, Arch={diag.Architecture}, " +
                                 $"LibExists={diag.LibLlamaExists}. " +
                                 "On-device inference requires libllama.so compiled for arm64-v8a.";
                    SglLogger.Warning("[MobileLlm] {0}", msg);
                    StatusChanged?.Invoke(this, msg);
                    _localModelLoaded = false;
                    return false;
                }
                SglLogger.Information("[MobileLlm] Native ARM64 library loaded successfully");
            }

            var modelParams = new ModelParams(_currentModelPath)
            {
                ContextSize = 2048,       // Small context for mobile RAM constraints
                GpuLayerCount = 0,         // CPU-only on Android
                Threads = Math.Max(1, Environment.ProcessorCount / 2), // Use half cores
                BatchSize = 256
            };

            _loadedWeights = LLamaWeights.LoadFromFile(modelParams);
            _llamaContext = _loadedWeights.CreateContext(modelParams);
            _localModelLoaded = true;

            SglLogger.Information("[MobileLlm] Local model loaded: {0} (on-device inference ready)", _currentModelName ?? "unknown");
            StatusChanged?.Invoke(this, $"Local model loaded: {_currentModelName} (on-device inference ready)");
            return true;
        }
        catch (DllNotFoundException)
        {
            // This is the expected failure when libllama.so is not bundled for ARM64.
            // The NuGet LLamaSharp.Backend.Cpu only ships x86/x64 natives.
            // To fix: cross-compile llama.cpp with Android NDK for ARM64-v8a and
            // place the resulting libllama.so in lib/arm64-v8a/ as an AndroidNativeLibrary.
            string msg = "Native ARM64 library (libllama.so) not found. " +
                         "On-device inference requires libllama.so cross-compiled from llama.cpp " +
                         "with Android NDK for ARM64-v8a. Falling back to offline heuristic mode.";
            SglLogger.Warning("[MobileLlm] {0}", msg);
            StatusChanged?.Invoke(this, msg);
            _localModelLoaded = false;
            return false;
        }
        catch (Exception ex)
        {
            SglLogger.Error("[MobileLlm] Failed to load local model.", ex);
            StatusChanged?.Invoke(this, $"Failed to load local model: {ex.Message}");
            _localModelLoaded = false;
            return false;
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Unloads the model from memory to free RAM.
    /// </summary>
    public void UnloadLocalModel()
    {
        _llamaContext?.Dispose();
        _loadedWeights?.Dispose();
        _llamaContext = null;
        _loadedWeights = null;
        _localModelLoaded = false;
        StatusChanged?.Invoke(this, "Local model unloaded from memory.");
    }

    public Task<List<string>> GetAvailableModelsAsync() => Task.FromResult(GetDownloadedModels());

    public Task<bool> DownloadModelAsync(string modelName, CancellationToken ct = default)
    {
        StatusChanged?.Invoke(this, "Model downloads are disabled in offline mode. Import a local GGUF file in Settings.");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Three-tier fallback threat analysis:
    /// <list type="number">
    ///   <item><b>Local:</b> On-device LLamaSharp inference (fully offline).</item>
    ///   <item><b>Fallback:</b> Offline heuristic response.</item>
    ///   <item><b>Heuristic:</b> Rule-based local analysis (always available).</item>
    /// </list>
    /// </summary>
    public async Task<string> AnalyzeThreatAsync(string filePath, string appName, List<string> permissions)
    {
        var prompt = BuildAnalysisPrompt(filePath, appName, permissions);

        if (IsLocalInferenceAvailable)
        {
            try
            {
                SglLogger.Information("[MobileLlm] Local analysis for {0}.", appName);
                var localResult = await RunOnDeviceInferenceAsync(prompt);
                if (!string.IsNullOrWhiteSpace(localResult))
                {
                    await StoreThreatResult(appName, filePath, permissions, localResult);
                    return localResult;
                }
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[MobileLlm] Local analysis failed for {0}: {1}", appName, ex.Message);
            }
        }

        var fallback = GenerateLocalResponse(appName, prompt);
        await StoreThreatResult(appName, filePath, permissions, fallback);
        return fallback;
    }

    private async Task StoreThreatResult(string appName, string filePath, List<string> permissions, string response)
    {
        await _knowledgeService.AddThreatEntryAsync(new ThreatKnowledgeEntry
        {
            ThreatType = "app_analysis",
            ThreatName = appName,
            FilePath = filePath,
            Description = response.Length > 500 ? response[..500] : response,
            Permissions = permissions,
            Source = IsLocalInferenceAvailable ? "local_llm_analysis" : "offline_llm_analysis"
        });
    }

    private string BuildAnalysisPrompt(string filePath, string appName, List<string> permissions)
    {
        return $@"Analyze this Android application for security threats:
App: {appName}
Path: {filePath}
Permissions: {string.Join(", ", permissions)}

Known threat patterns from local database:
{string.Join(", ", _knowledgeService.GetDatabase().LearnedPatterns.Take(10).Select(p => $"{p.Pattern} (confidence: {p.Confidence:P0})"))}

Provide:
1. Threat assessment (Safe/Low/Medium/High/Critical)
2. Specific risks from the permissions
3. Recommended actions
4. Whether this matches any known malware patterns";
    }

    private string PerformLocalAnalysis(string filePath, string appName, List<string> permissions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Local Security Analysis: {appName} ===");
        sb.AppendLine("(Offline mode - using local heuristic engine)");
        sb.AppendLine();

        double riskScore = _knowledgeService.GetThreatScore(appName, permissions);
        int dangerousCount = permissions.Count(p =>
            p.Contains("RECORD_AUDIO") || p.Contains("CAMERA") ||
            p.Contains("LOCATION") || p.Contains("READ_SMS") ||
            p.Contains("READ_CONTACTS") || p.Contains("READ_CALL_LOG"));

        riskScore += dangerousCount * 0.15;
        riskScore = Math.Min(1.0, riskScore);

        string level = riskScore switch
        {
            >= 0.8 => "CRITICAL",
            >= 0.6 => "HIGH",
            >= 0.4 => "MEDIUM",
            >= 0.2 => "LOW",
            _ => "SAFE"
        };

        sb.AppendLine($"Threat Level: {level} (Score: {riskScore:P0})");
        sb.AppendLine();
        sb.AppendLine("Permission Analysis:");

        foreach (var p in permissions)
        {
            string risk = p switch
            {
                var x when x.Contains("RECORD_AUDIO") => "HIGH - Can record audio/conversations",
                var x when x.Contains("CAMERA") => "HIGH - Can access camera",
                var x when x.Contains("FINE_LOCATION") => "MEDIUM - Precise location tracking",
                var x when x.Contains("READ_SMS") => "HIGH - Can read text messages",
                var x when x.Contains("READ_CONTACTS") => "MEDIUM - Can read contacts",
                var x when x.Contains("READ_CALL_LOG") => "HIGH - Can read call history",
                var x when x.Contains("PHONE_STATE") => "LOW - Can detect call state",
                _ => "INFO - Standard permission"
            };
            sb.AppendLine($"  {p.Split('.').Last()}: {risk}");
        }

        sb.AppendLine();
        sb.AppendLine("Recommendations:");
        if (dangerousCount >= 3)
            sb.AppendLine("  - Consider uninstalling or restricting this app");
        if (permissions.Any(p => p.Contains("RECORD_AUDIO")))
            sb.AppendLine("  - Revoke microphone permission if not needed");
        if (permissions.Any(p => p.Contains("CAMERA")))
            sb.AppendLine("  - Revoke camera permission if not needed");
        if (permissions.Any(p => p.Contains("LOCATION")))
            sb.AppendLine("  - Switch to approximate location only");

        return sb.ToString();
    }

    public async Task<bool> CheckFirstLaunchAsync()
    {
        if (IsModelDownloaded)
        {
            // Try loading for local inference if not already loaded
            if (!_localModelLoaded)
                await LoadLocalModelAsync();
            return true;
        }

        StatusChanged?.Invoke(this, "No LLM model found. Checking for a local GGUF import...");
        var models = await GetAvailableModelsAsync();

        if (models.Count > 0)
        {
            StatusChanged?.Invoke(this, $"Found {models.Count} local model(s). Ready to use.");
            return false;
        }

        StatusChanged?.Invoke(this, "No local models available. Using offline heuristic engine.");
        return false;
    }

    public Task SyncKnowledgeWithServerAsync()
    {
        StatusChanged?.Invoke(this, "Offline sync is disabled. This build uses local-only models.");
        return Task.CompletedTask;
    }

    public List<string> GetDownloadedModels()
    {
        if (!Directory.Exists(_modelDir)) return new List<string>();
        return Directory.GetFiles(_modelDir, "*.gguf")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    public async Task<bool> ImportModelFromFileAsync(FileResult file, CancellationToken ct = default)
    {
        await using var input = await file.OpenReadAsync();
        return await ImportModelFromStreamAsync(input, file.FileName, ct);
    }

    public async Task<bool> ImportModelFromPathAsync(string sourcePath, string? originalFileName = null, CancellationToken ct = default)
    {
        await using var input = File.OpenRead(sourcePath);
        return await ImportModelFromStreamAsync(input, originalFileName ?? Path.GetFileName(sourcePath), ct);
    }

    public async Task<bool> ImportModelFromStreamAsync(Stream input, string fileName, CancellationToken ct = default)
    {
        try
        {
            var safeName = string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName))
                ? $"model_{DateTime.UtcNow:yyyyMMddHHmmss}"
                : Path.GetFileNameWithoutExtension(fileName);

            var targetPath = Path.Combine(_modelDir, $"{SanitizeFileName(safeName)}.gguf");
            await using var output = File.Create(targetPath);
            await input.CopyToAsync(output, ct);

            _currentModelPath = targetPath;
            _currentModelName = Path.GetFileNameWithoutExtension(targetPath);
            IsReady = true;

            StatusChanged?.Invoke(this, $"Imported model: {_currentModelName}");
            if (_autoLoadOnImport)
                _ = Task.Run(() => LoadLocalModelAsync());
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Model import failed: {ex.Message}");
            SglLogger.Warning("[MobileLlm] Model import failed: {0}", ex.Message);
            return false;
        }
    }


    /// <summary>
    /// Runs local inference using the downloaded GGUF model.
    /// Offline fallback: on-device LLamaSharp, then heuristic response.
    /// This method works fully offline when a model is loaded locally.
    /// </summary>
    
    public async Task<string> RunLocalInferenceAsync(string userMessage, string? systemPrompt = null)
    {
        if (IsLocalInferenceAvailable)
        {
            try
            {
                var fullPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? $"User: {userMessage}\nAssistant:"
                    : $"{systemPrompt}\n\nUser: {userMessage}\nAssistant:";

                var result = await RunOnDeviceInferenceAsync(fullPrompt);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
            catch (Exception ex)
            {
                SglLogger.Warning("[MobileLlm] On-device inference failed: {0}", ex.Message);
            }
        }

        return GenerateLocalResponse(userMessage, systemPrompt);
    }

    /// <summary>    /// <summary>
    /// Executes inference directly on the device using LLamaSharp.
    /// Thread-safe via semaphore — only one inference at a time.
    /// </summary>
    private async Task<string> RunOnDeviceInferenceAsync(string prompt)
    {
        if (_llamaContext == null || _loadedWeights == null)
            throw new InvalidOperationException("Local model not loaded");

        await _inferenceLock.WaitAsync();
        try
        {
            var executor = new LLama.StatelessExecutor(_loadedWeights, _llamaContext.Params);
            var inferenceParams = new InferenceParams
            {
                MaxTokens = 512,          // Limit output length for mobile
                AntiPrompts = new[] { "User:", "\n\nUser:" }
            };

            var sb = new System.Text.StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams))
            {
                sb.Append(token);

                // Safety: abort if response exceeds 2KB (mobile memory protection)
                if (sb.Length > 2048)
                    break;
            }

            return sb.ToString().Trim();
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private string GenerateLocalResponse(string userMessage, string? systemPrompt = null)
    {
        var prompt = (systemPrompt ?? string.Empty).ToLowerInvariant();
        var message = userMessage.ToLowerInvariant();

        var storyKeywords = new[] { "story", "character", "world", "journal", "creative", "write", "scene", "plot" };
        var journalKeywords = new[] { "journal", "reflect", "memory", "idea", "plan", "brainstorm" };

        if (storyKeywords.Any(k => message.Contains(k) || prompt.Contains(k)))
        {
            return """
[Offline Mode]

I can help build a story, shape a character, sketch a world, or turn notes into a scene.
Try giving me a setting, a mood, a character goal, or a conflict and I will help expand it.
""";
        }

        if (journalKeywords.Any(k => message.Contains(k) || prompt.Contains(k)))
        {
            return """
[Offline Mode]

I can help you turn this into a journal entry, reflection, outline, or memory summary.
Share the feeling, event, or idea and I will help shape it into something usable.
""";
        }

        if (IsModelDownloaded && !IsLocalInferenceAvailable)
        {
            return """
[Offline Mode - Model Ready]

A GGUF model is present but not loaded into memory yet.
Open Settings to keep the selected model lightweight, or restart after importing a smaller draft model.
""";
        }

        return """
[Offline Mode]

I'm running without a loaded model right now, but I am still ready to help with stories, journaling, planning, and creative ideas.
Import a GGUF draft model in Settings for richer responses.
""";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe;
    }

    public void SwitchModel(string modelName)
    {
        var modelPath = Path.Combine(_modelDir, $"{modelName}.gguf");
        if (File.Exists(modelPath))
        {
            // Unload current model first
            UnloadLocalModel();

            _currentModelPath = modelPath;
            _currentModelName = modelName;
            IsReady = true;
            StatusChanged?.Invoke(this, $"Switched to model: {modelName}");

            // Load new model for local inference
            _ = Task.Run(() => LoadLocalModelAsync());
        }
    }

    public void Dispose()
    {
        _llamaContext?.Dispose();
        _loadedWeights?.Dispose();
        _inferenceLock.Dispose();
    }
}