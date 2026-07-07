using MCOfflineChat.Mobile.ViewModels;

namespace MCOfflineChat.Mobile.Views;

public partial class QrSharePage : ContentPage
{
    public QrSharePage(QrShareViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
