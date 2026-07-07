using MCOfflineChat.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCOfflineChat.Mobile.Views;

public partial class ChatPage : ContentPage
{
    public ChatPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (BindingContext is not ChatViewModel)
        {
            var vm = Handler?.MauiContext?.Services.GetService<ChatViewModel>();
            if (vm != null)
            {
                BindingContext = vm;
            }
        }
    }
}
