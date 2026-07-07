using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class WifiScannerPage : ContentPage
{
    public WifiScannerPage(WifiScannerViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
