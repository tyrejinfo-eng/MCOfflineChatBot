using MCOfflineChat.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCOfflineChat.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel? _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (_viewModel == null)
        {
            _viewModel = Handler?.MauiContext?.Services.GetService<SettingsViewModel>();
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
