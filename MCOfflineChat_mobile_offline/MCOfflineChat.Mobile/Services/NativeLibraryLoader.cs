using System.Runtime.InteropServices;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

/// <summary>
/// Native library loader for Android ARM64. Ensures libllama.so is loaded before LLamaSharp
/// attempts P/Invoke. On Android, native libraries must be loaded via dlopen from the app's
/// native library directory (jniLibs/arm64-v8a/).
///
/// Build Instructions for libllama.so:
/// 1. Clone llama.cpp: git clone https://github.com/ggerganov/llama.cpp
/// 2. Install Android NDK r26+
/// 3. Cross-compile:
///    mkdir build-android &amp;&amp; cd build-android
///    cmake .. -DCMAKE_TOOLCHAIN_FILE=$NDK/build/cmake/android.toolchain.cmake \
///             -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-24 \
///             -DBUILD_SHARED_LIBS=ON -DLLAMA_NATIVE=OFF
///    cmake --build . --config Release
/// 4. Copy build-android/bin/libllama.so to lib/arm64-v8a/libllama.so in the Mobile project
/// </summary>
public static class NativeLibraryLoader
{
#if ANDROID
    // Android uses libdl for dynamic loading
    [DllImport("libdl.so", EntryPoint = "dlopen")]
    private static extern IntPtr DlOpen(string? fileName, int flags);

    [DllImport("libdl.so", EntryPoint = "dlerror")]
    private static extern IntPtr DlError();

    [DllImport("libdl.so", EntryPoint = "dlclose")]
    private static extern int DlClose(IntPtr handle);

    private const int RTLD_NOW = 2;
    private const int RTLD_GLOBAL = 0x100;
#endif

    private static bool _isLoaded;
    private static IntPtr _handle = IntPtr.Zero;
    private static readonly object _lock = new();

    /// <summary>
    /// Whether the native libllama.so has been successfully loaded.
    /// </summary>
    public static bool IsNativeLibraryLoaded => _isLoaded;

    /// <summary>
    /// Attempts to load the native libllama.so library for ARM64 Android.
    /// Must be called before any LLamaSharp operations.
    /// Returns true if the library was loaded or was already loaded.
    /// </summary>
    public static bool TryLoadNativeLibrary()
    {
        if (_isLoaded) return true;

        lock (_lock)
        {
            if (_isLoaded) return true;

#if ANDROID
            try
            {
                // On Android, native libs bundled as AndroidNativeLibrary are extracted
                // to the app's native library directory. System.loadLibrary() / dlopen
                // searches this directory automatically when given the short name.

                // Try direct load by short name (Android's linker searches jniLibs)
                _handle = DlOpen("libllama.so", RTLD_NOW | RTLD_GLOBAL);

                if (_handle != IntPtr.Zero)
                {
                    _isLoaded = true;
                    SglLogger.Information("[NativeLoader] libllama.so loaded successfully via dlopen");
                    return true;
                }

                // Get error message
                var errorPtr = DlError();
                var error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "unknown error";
                SglLogger.Warning("[NativeLoader] dlopen('libllama.so') failed: {0}", error ?? "null");

                // Fallback: try loading from the app's native lib directory explicitly
                var nativeDir = GetNativeLibraryDirectory();
                if (nativeDir != null)
                {
                    var fullPath = Path.Combine(nativeDir, "libllama.so");
                    if (File.Exists(fullPath))
                    {
                        _handle = DlOpen(fullPath, RTLD_NOW | RTLD_GLOBAL);
                        if (_handle != IntPtr.Zero)
                        {
                            _isLoaded = true;
                            SglLogger.Information("[NativeLoader] libllama.so loaded from: {0}", fullPath);
                            return true;
                        }
                        errorPtr = DlError();
                        error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "unknown";
                        SglLogger.Warning("[NativeLoader] dlopen('{0}') failed: {1}", fullPath, error ?? "null");
                    }
                    else
                    {
                        SglLogger.Warning("[NativeLoader] libllama.so not found at: {0}", fullPath);
                    }
                }

                // Fallback: check if user sideloaded to app data directory
                var appDataPath = Path.Combine(FileSystem.AppDataDirectory, "native", "libllama.so");
                if (File.Exists(appDataPath))
                {
                    _handle = DlOpen(appDataPath, RTLD_NOW | RTLD_GLOBAL);
                    if (_handle != IntPtr.Zero)
                    {
                        _isLoaded = true;
                        SglLogger.Information("[NativeLoader] libllama.so loaded from sideload: {0}", appDataPath);
                        return true;
                    }
                }

                SglLogger.Warning("[NativeLoader] libllama.so could not be loaded. On-device LLM inference unavailable. " +
                    "To fix: cross-compile llama.cpp with Android NDK for arm64-v8a and add to lib/arm64-v8a/");
                return false;
            }
            catch (Exception ex)
            {
                SglLogger.Error("[NativeLoader] Exception during native library load", ex);
                return false;
            }
#else
            // Non-Android platforms: .NET runtime handles P/Invoke resolution
            _isLoaded = true;
            return true;
#endif
        }
    }

    /// <summary>
    /// Checks whether the device CPU architecture supports ARM64.
    /// </summary>
    public static bool IsArm64Device()
    {
#if ANDROID
        try
        {
            var abi = Android.OS.Build.SupportedAbis;
            if (abi != null)
            {
                foreach (var a in abi)
                {
                    if (a == "arm64-v8a") return true;
                }
            }
            return false;
        }
        catch
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        }
#else
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
#endif
    }

    /// <summary>
    /// Gets diagnostic information about the native library loading state.
    /// </summary>
    public static NativeLibraryDiagnostics GetDiagnostics()
    {
        var diag = new NativeLibraryDiagnostics
        {
            IsLoaded = _isLoaded,
            IsArm64 = IsArm64Device(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsDescription = RuntimeInformation.OSDescription
        };

#if ANDROID
        var nativeDir = GetNativeLibraryDirectory();
        if (nativeDir != null)
        {
            diag.NativeLibraryDirectory = nativeDir;
            diag.LibLlamaExists = File.Exists(Path.Combine(nativeDir, "libllama.so"));
        }

        var sideloadPath = Path.Combine(FileSystem.AppDataDirectory, "native", "libllama.so");
        diag.SideloadPath = sideloadPath;
        diag.SideloadExists = File.Exists(sideloadPath);
#endif

        return diag;
    }

#if ANDROID
    private static string? GetNativeLibraryDirectory()
    {
        try
        {
            var context = Android.App.Application.Context;
            var appInfo = context.ApplicationInfo;
            return appInfo?.NativeLibraryDir;
        }
        catch
        {
            return null;
        }
    }
#endif

    /// <summary>
    /// Unloads the native library (if loaded). Primarily for testing.
    /// </summary>
    public static void Unload()
    {
        lock (_lock)
        {
#if ANDROID
            if (_handle != IntPtr.Zero)
            {
                DlClose(_handle);
                _handle = IntPtr.Zero;
            }
#endif
            _isLoaded = false;
        }
    }
}

/// <summary>Native library diagnostics for debugging.</summary>
public sealed class NativeLibraryDiagnostics
{
    public bool IsLoaded { get; set; }
    public bool IsArm64 { get; set; }
    public string Architecture { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public string? NativeLibraryDirectory { get; set; }
    public bool LibLlamaExists { get; set; }
    public string? SideloadPath { get; set; }
    public bool SideloadExists { get; set; }
}
