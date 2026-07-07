using MCOfflineChat.Mobile.Services;
using Xunit;

namespace MCOfflineChat.Tests;

public sealed class ShellNavigationTests
{
    [Fact]
    public void VisibleNavigation_Matches_Offline_First_App()
    {
        var tabs = NavigationManifest.Tabs.Select(t => t.Title).ToArray();

        Assert.Contains("Dashboard", tabs);
        Assert.Contains("Chat", tabs);
        Assert.Contains("Documents", tabs);
        Assert.Contains("Stories", tabs);
        Assert.Contains("History", tabs);
        Assert.Contains("Git Search", tabs);
        Assert.Contains("Telemetry", tabs);
        Assert.Contains("Settings", tabs);

        Assert.DoesNotContain("LoginPage", tabs);
        Assert.DoesNotContain("LoginPage", NavigationManifest.HiddenLegacyRoutes);
    }
}
