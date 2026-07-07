using MCOfflineChat.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCOfflineChat.Mobile.Views;

public partial class HistoryPage : ContentPage
{
    private HistoryViewModel? _viewModel;

    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (_viewModel == null)
        {
            _viewModel = Handler?.MauiContext?.Services.GetService<HistoryViewModel>();
            if (_viewModel != null)
                BindingContext = _viewModel;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel != null)
            await _viewModel.OnAppearingAsync();
    }
}
