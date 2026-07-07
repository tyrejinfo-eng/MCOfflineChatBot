using MCOfflineChat.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCOfflineChat.Mobile.Views;

public partial class ScanPage : ContentPage
{
    public ScanPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (BindingContext is not ScanViewModel)
        {
            var vm = Handler?.MauiContext?.Services.GetService<ScanViewModel>();
            if (vm != null)
            {
                BindingContext = vm;
            }
        }
    }
}
