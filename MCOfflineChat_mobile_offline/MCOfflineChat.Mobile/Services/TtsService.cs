using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

#if ANDROID
using Android.Speech.Tts;
using Application = Android.App.Application;
#endif

namespace MCOfflineChat.Mobile.Services;

/// <summary>
/// Offline text-to-speech service backed by the device's native TTS engine.
/// </summary>
public sealed class TtsService : IDisposable
{
    private readonly AppPreferences _prefs;
    private bool _nativeTtsReady;
    private bool _isDisposed;

#if ANDROID
    private TextToSpeech? _nativeTts;
    private TtsInitListener? _ttsListener;
#endif

    public bool IsEnabled => _prefs.TtsEnabled;
    public bool IsReady => _nativeTtsReady;
    public string Status { get; private set; } = "Initializing...";

    public event EventHandler<string>? StatusChanged;

    public TtsService(AppPreferences prefs)
    {
        _prefs = prefs;
        InitializeNativeTts();
    }

    private void InitializeNativeTts()
    {
#if ANDROID
        try
        {
            var context = Application.Context;
            if (context == null)
            {
                Status = "TTS unavailable — no Android context";
                StatusChanged?.Invoke(this, Status);
                return;
            }

            _ttsListener = new TtsInitListener(success =>
            {
                _nativeTtsReady = success;
                Status = success ? "TTS ready" : "TTS init failed";
                StatusChanged?.Invoke(this, Status);
            });

            _nativeTts = new TextToSpeech(context, _ttsListener);
            _nativeTts.SetLanguage(Java.Util.Locale.Us);
            _nativeTts.SetSpeechRate(0.95f);
            _nativeTts.SetPitch(1.0f);
        }
        catch (Exception ex)
        {
            Status = $"TTS init error: {ex.Message}";
            StatusChanged?.Invoke(this, Status);
        }
#else
        Status = "TTS not available on this platform";
        StatusChanged?.Invoke(this, Status);
#endif
    }

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        SpeakNative(text);
        return Task.CompletedTask;
    }

    private void SpeakNative(string text)
    {
#if ANDROID
        if (!_nativeTtsReady || _nativeTts == null) return;

        try
        {
            _nativeTts.Stop();
            _nativeTts.Speak(text, QueueMode.Flush, null, Guid.NewGuid().ToString("N")[..8]);
        }
        catch (Exception ex)
        {
            Status = $"TTS speak error: {ex.Message}";
            StatusChanged?.Invoke(this, Status);
        }
#endif
    }

    public void Stop()
    {
#if ANDROID
        try { _nativeTts?.Stop(); } catch (Exception ex) { Status = $"TTS stop error: {ex.Message}"; StatusChanged?.Invoke(this, Status); SglLogger.Warning("[TTS] Stop failed: {0}", ex.Message); }
#endif
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
#if ANDROID
        try { _nativeTts?.Shutdown(); _nativeTts?.Dispose(); } catch (Exception ex) { Status = $"TTS dispose error: {ex.Message}"; StatusChanged?.Invoke(this, Status); SglLogger.Warning("[TTS] Dispose failed: {0}", ex.Message); }
#endif
    }

#if ANDROID
    private sealed class TtsInitListener : Java.Lang.Object, IOnInitListener
    {
        private readonly Action<bool> _callback;
        public TtsInitListener(Action<bool> callback) => _callback = callback;
        public void OnInit(OperationResult status) => _callback(status == OperationResult.Success);
    }
#endif
}
