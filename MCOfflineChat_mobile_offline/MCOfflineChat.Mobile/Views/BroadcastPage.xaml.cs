using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class BroadcastPage : ContentPage
{
    private readonly BroadcastViewModel _vm;

    public BroadcastPage(BroadcastViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.OnDisappearing();
    }
}
