using MCOfflineChat.Mobile.Models;
using MCOfflineChat.Shared.Logging;

namespace MCOfflineChat.Mobile.Services;

public class DeviceProtectionService
{
    public event EventHandler<string>? StatusChanged;

    public class ProtectionStatus
    {
        public bool MicrophoneBlocked { get; set; }
        public bool CameraBlocked { get; set; }
        public List<AppAccessInfo> MicrophoneAccessApps { get; set; } = new();
        public List<AppAccessInfo> CameraAccessApps { get; set; } = new();
    }

    public class AppAccessInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public bool IsCurrentlyUsing { get; set; }
        public bool HasPermission { get; set; }
        public bool IsSystemApp { get; set; }
        public DateTime? LastUsed { get; set; }
    }

    public async Task<ProtectionStatus> ScanDeviceAccessAsync()
    {
        var status = new ProtectionStatus();

#if ANDROID
        await Task.Run(() =>
        {
            try
            {
                var context = Android.App.Application.Context;
                var pm = context.PackageManager;
                if (pm == null) return;

                var packages = pm.GetInstalledPackages(Android.Content.PM.PackageInfoFlags.Permissions);
                if (packages == null) return;

                foreach (var pkg in packages)
                {
                    if (pkg.PackageName == null || pkg.RequestedPermissions == null) continue;

                    var perms = pkg.RequestedPermissions;
                    var appInfo = pkg.ApplicationInfo;
                    bool isSystem = appInfo != null &&
                        (appInfo.Flags & Android.Content.PM.ApplicationInfoFlags.System) != 0;

                    string appName = appInfo?.LoadLabel(pm)?.ToString() ?? pkg.PackageName;

                    if (perms.Any(p => p == "android.permission.RECORD_AUDIO"))
                    {
                        status.MicrophoneAccessApps.Add(new AppAccessInfo
                        {
                            PackageName = pkg.PackageName,
                            AppName = appName,
                            HasPermission = true,
                            IsSystemApp = isSystem
                        });
                    }

                    if (perms.Any(p => p == "android.permission.CAMERA"))
                    {
                        status.CameraAccessApps.Add(new AppAccessInfo
                        {
                            PackageName = pkg.PackageName,
                            AppName = appName,
                            HasPermission = true,
                            IsSystemApp = isSystem
                        });
                    }
                }

                status.MicrophoneAccessApps = status.MicrophoneAccessApps
                    .OrderByDescending(a => !a.IsSystemApp)
                    .ThenBy(a => a.AppName).ToList();
                status.CameraAccessApps = status.CameraAccessApps
                    .OrderByDescending(a => !a.IsSystemApp)
                    .ThenBy(a => a.AppName).ToList();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Scan error: {ex.Message}");
            }
        });
#endif

        return status;
    }

    public async Task<bool> KillAppAsync(string packageName)
    {
#if ANDROID
        try
        {
            var am = (Android.App.ActivityManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService);
            am?.KillBackgroundProcesses(packageName);
            StatusChanged?.Invoke(this, $"Killed {packageName}");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Kill failed: {ex.Message}");
        }
#endif
        await Task.CompletedTask;
        return false;
    }

    public async Task OpenPermissionSettingsAsync(string packageName)
    {
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));
            Android.App.Application.Context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Open settings failed: {ex.Message}");
            SglLogger.Warning("[DeviceProtection] Open permission settings failed: {0}", ex.Message);
        }
#endif
        await Task.CompletedTask;
    }

    public async Task OpenMicrophoneSettingsAsync()
    {
#if ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                // Open the permission manager for the microphone permission group
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionPrivacySettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                intent.PutExtra(Android.Content.Intent.ExtraPermissionGroupName,
                    Android.Manifest.Permission_group.Microphone);
                Android.App.Application.Context.StartActivity(intent);
            }
            else
            {
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionSettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Microphone settings fallback failed: {ex.Message}");
            SglLogger.Warning("[DeviceProtection] Microphone settings fallback failed: {0}", ex.Message);
            try
            {
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionSettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception fallbackEx)
            {
                StatusChanged?.Invoke(this, $"Microphone settings fallback unavailable: {fallbackEx.Message}");
                SglLogger.Warning("[DeviceProtection] Microphone settings final fallback failed: {0}", fallbackEx.Message);
            }
        }
#endif
        await Task.CompletedTask;
    }

    public async Task OpenCameraSettingsAsync()
    {
#if ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                // Open the permission manager for the camera permission group
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionPrivacySettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                intent.PutExtra(Android.Content.Intent.ExtraPermissionGroupName,
                    Android.Manifest.Permission_group.Camera);
                Android.App.Application.Context.StartActivity(intent);
            }
            else
            {
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionSettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Camera settings fallback failed: {ex.Message}");
            SglLogger.Warning("[DeviceProtection] Camera settings fallback failed: {0}", ex.Message);
            try
            {
                var intent = new Android.Content.Intent(Android.Provider.Settings.ActionSettings);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception fallbackEx)
            {
                StatusChanged?.Invoke(this, $"Camera settings fallback unavailable: {fallbackEx.Message}");
                SglLogger.Warning("[DeviceProtection] Camera settings final fallback failed: {0}", fallbackEx.Message);
            }
        }
#endif
        await Task.CompletedTask;
    }
}