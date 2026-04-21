using Locora.App.ViewModels;
using Locora.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["services"] = typeof(ServicesPage),
        ["domains"] = typeof(DomainsPage),
        ["diagnostics"] = typeof(DiagnosticsPage),
        ["logs"] = typeof(LogsPage),
        ["settings"] = typeof(SettingsPage)
    };

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.GetService<MainWindowViewModel>();

        InitializeComponent();
        DataContext = ViewModel;

        NavigateTo("dashboard");

        _ = ViewModel.InitializeAsync();
    }

    public void NavigateTo(string tag)
    {
        if (!_pages.TryGetValue(tag, out var pageType))
        {
            return;
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        var selectedItem = ShellNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));

        if (selectedItem is not null && !ReferenceEquals(ShellNavigationView.SelectedItem, selectedItem))
        {
            ShellNavigationView.SelectedItem = selectedItem;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
    }
}
