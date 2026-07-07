using MCOfflineChat.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCOfflineChat.Mobile.Views;

public partial class StoriesPage : ContentPage
{
    private StoriesViewModel? _viewModel;

    public StoriesPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (_viewModel == null)
        {
            _viewModel = Handler?.MauiContext?.Services.GetService<StoriesViewModel>();
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
