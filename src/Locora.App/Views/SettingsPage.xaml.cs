using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class SettingsPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }
}
