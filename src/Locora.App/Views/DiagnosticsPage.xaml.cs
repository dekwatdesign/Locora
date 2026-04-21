using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class DiagnosticsPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }
}
