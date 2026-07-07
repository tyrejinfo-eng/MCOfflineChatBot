namespace MCOfflineChat.Mobile;

public partial class App : Application
{
    public static string Version => MCOfflineChat.Shared.VersionInfo.MobileVersion;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
