using Locora.App.Contracts;
using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class AdvancedPage : Page, IRouteAwarePage
{
    public MainWindowViewModel ViewModel { get; }

    public AdvancedPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }

    public void ApplyRoute(LocoraShellRoute route)
    {
        AdvancedTabs.SelectedIndex = route.Section switch
        {
            "packages" => 1,
            "updates" => 2,
            "migration" => 3,
            "logs" => 4,
            _ => 0
        };
    }
}
