namespace MCOfflineChat.Mobile.Services;

public sealed record ShellTabDescriptor(string Route, string Title, string Icon);

public static class NavigationManifest
{
    public static readonly ShellTabDescriptor[] Tabs =
    [
        new("DashboardPage", "Dashboard", "icon_dashboard.svg"),
        new("ChatPage", "Chat", "icon_chat.svg"),
        new("DocumentsPage", "Documents", "icon_scanner.svg"),
        new("StoriesPage", "Stories", "icon_dashboard.svg"),
        new("HistoryPage", "History", "icon_history.svg"),
        new("GitPage", "Git Search", "icon_scanner.svg"),
        new("TelemetryPage", "Telemetry", "icon_scanner.svg"),
        new("SettingsPage", "Settings", "icon_settings.svg")
    ];

    public static readonly string[] HiddenLegacyRoutes =
    [
        "SwarmStoragePage",
        "BroadcastPage",
        "AlertsPage",
        "DeviceProtectionPage",
        "QrSharePage",
        "ScanPage",
        "WifiScannerPage",
        "EngineStatusPage",
        "FaqPage",
        "BluetoothPage"
    ];
}
