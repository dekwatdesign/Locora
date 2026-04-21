using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class DashboardPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }
}
