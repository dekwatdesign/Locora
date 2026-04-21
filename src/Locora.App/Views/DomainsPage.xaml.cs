using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class DomainsPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public DomainsPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }
}
