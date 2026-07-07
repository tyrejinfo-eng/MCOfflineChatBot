using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class SwarmStoragePage : ContentPage
{
    private readonly SwarmStorageViewModel _viewModel;

    public SwarmStoragePage(SwarmStorageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }
}
