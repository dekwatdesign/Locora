using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class LogsPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public LogsPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }
}
