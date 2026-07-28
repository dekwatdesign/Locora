using Locora.App.Contracts;
using Locora.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class HealthPage : Page, IRouteAwarePage
{
    public MainWindowViewModel ViewModel { get; }

    public HealthPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }

    public void ApplyRoute(LocoraShellRoute route)
    {
        HealthTabs.SelectedIndex = route.Section switch
        {
            "domains" or "ssl" => 1,
            "ports" or "permissions" => 2,
            "tunnels" => 3,
            "reports" => 4,
            _ => 0
        };
    }
}
