using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class TelemetryPage : ContentPage
{
    public TelemetryPage(TelemetryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
