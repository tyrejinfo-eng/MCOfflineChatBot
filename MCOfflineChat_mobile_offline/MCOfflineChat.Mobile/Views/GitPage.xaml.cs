using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class GitPage : ContentPage
{
    public GitPage(GitViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
