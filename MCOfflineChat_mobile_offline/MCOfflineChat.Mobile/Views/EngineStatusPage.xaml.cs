using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class EngineStatusPage : ContentPage
{
    private readonly EngineStatusViewModel _viewModel;

    public EngineStatusPage(EngineStatusViewModel viewModel)
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
