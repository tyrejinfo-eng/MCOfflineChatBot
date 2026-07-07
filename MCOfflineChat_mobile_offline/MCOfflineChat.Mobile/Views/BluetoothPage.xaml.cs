using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class BluetoothPage : ContentPage
{
    public BluetoothPage(BluetoothViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
