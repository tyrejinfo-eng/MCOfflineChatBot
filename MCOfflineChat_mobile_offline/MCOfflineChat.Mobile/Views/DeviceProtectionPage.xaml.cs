using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class DeviceProtectionPage : ContentPage
{
    public DeviceProtectionPage(DeviceProtectionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
