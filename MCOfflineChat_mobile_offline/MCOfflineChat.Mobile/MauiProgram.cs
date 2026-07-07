using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using MCOfflineChat.Mobile.Services;
using MCOfflineChat.Mobile.ViewModels;
using MCOfflineChat.Mobile.Views;
using ZXing.Net.Maui.Controls;

namespace MCOfflineChat.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<AppPreferences>();
        builder.Services.AddSingleton<HistoryLibraryService>();
        builder.Services.AddSingleton<WorkContextService>();
        builder.Services.AddSingleton<TtsService>();
        builder.Services.AddSingleton<ThreatKnowledgeService>();
        builder.Services.AddSingleton<BluetoothService>();
        builder.Services.AddSingleton<WifiScannerService>();
        builder.Services.AddSingleton<TelemetryService>();
        builder.Services.AddSingleton<DeviceProtectionService>();
        builder.Services.AddSingleton<GitService>();
        builder.Services.AddSingleton<MobileLlmService>();
        builder.Services.AddSingleton(sp => new LocalDocumentService(null, sp.GetRequiredService<HistoryLibraryService>()));
        builder.Services.AddSingleton(sp => new StoryLibraryService(null, sp.GetRequiredService<HistoryLibraryService>()));
        builder.Services.AddSingleton(sp => new ChatSessionService(null, sp.GetRequiredService<HistoryLibraryService>()));

        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<TelemetryViewModel>();
        builder.Services.AddSingleton<GitViewModel>();
        builder.Services.AddSingleton<FaqViewModel>();
        builder.Services.AddSingleton<DocumentsViewModel>();
        builder.Services.AddSingleton<StoriesViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();

        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<TelemetryPage>();
        builder.Services.AddTransient<GitPage>();
        builder.Services.AddTransient<FaqPage>();
        builder.Services.AddTransient<DocumentsPage>();
        builder.Services.AddTransient<StoriesPage>();
        builder.Services.AddTransient<HistoryPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
