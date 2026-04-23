using Locora.App.Models;
using Locora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Views;

public sealed partial class ServicesPage : Page
{
    public MainWindowViewModel ViewModel { get; }

    public ServicesPage()
    {
        ViewModel = App.GetService<MainWindowViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
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

    private void OnStartServicePresetClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServicePresetCard preset } &&
            ViewModel.StartServicePresetCommand.CanExecute(preset))
        {
            ViewModel.StartServicePresetCommand.Execute(preset);
        }
    }

    private void OnStopServicePresetClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServicePresetCard preset } &&
            ViewModel.StopServicePresetCommand.CanExecute(preset))
        {
            ViewModel.StopServicePresetCommand.Execute(preset);
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

    private void OnOpenMailpitInboxClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard { IsMailpitService: true } } &&
            ViewModel.OpenMailpitInboxCommand.CanExecute(null))
        {
            ViewModel.OpenMailpitInboxCommand.Execute(null);
        }
    }

    private void OnCopyMailpitSmtpClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard { IsMailpitService: true } } &&
            ViewModel.CopyMailpitSmtpConfigCommand.CanExecute(null))
        {
            ViewModel.CopyMailpitSmtpConfigCommand.Execute(null);
        }
    }

    private void OnOpenDatabaseAdminClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard { SupportsDatabaseAdminTool: true } service } &&
            ViewModel.OpenDatabaseAdminToolCommand.CanExecute(service))
        {
            ViewModel.OpenDatabaseAdminToolCommand.Execute(service);
        }
    }

    private void OnCopyDatabaseConnectionClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard { SupportsDatabaseAdminTool: true } service } &&
            ViewModel.CopyDatabaseConnectionCommand.CanExecute(service))
        {
            ViewModel.CopyDatabaseConnectionCommand.Execute(service);
        }
    }

    private void OnOpenDatabaseDetailsClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ServiceStatusCard { SupportsDatabaseAdminTool: true } service } &&
            ViewModel.OpenDatabaseConnectionDetailsCommand.CanExecute(service))
        {
            ViewModel.OpenDatabaseConnectionDetailsCommand.Execute(service);
        }
    }
}
