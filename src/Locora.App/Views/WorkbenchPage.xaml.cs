using Locora.App.Contracts;
using Locora.App.Models;
using Locora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class WorkbenchPage : Page, IRouteAwarePage
{
    public MainWindowViewModel ViewModel { get; }

    public WorkbenchPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
    }

    public void ApplyRoute(LocoraShellRoute route)
    {
        WorkbenchTabs.SelectedIndex = route.Section switch
        {
            "services" => 1,
            "stack" => 2,
            _ => 0
        };
    }

    private void OnStartServiceClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard service } &&
            ViewModel.StartServiceCommand.CanExecute(service))
        {
            ViewModel.StartServiceCommand.Execute(service);
        }
    }

    private void OnStopServiceClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard service } &&
            ViewModel.StopServiceCommand.CanExecute(service))
        {
            ViewModel.StopServiceCommand.Execute(service);
        }
    }

    private void OnRepairServiceClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard service } &&
            ViewModel.RepairServiceCommand.CanExecute(service))
        {
            ViewModel.RepairServiceCommand.Execute(service);
        }
    }
}
