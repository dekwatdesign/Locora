using Locora.App.ViewModels;
using Locora.Application.Abstractions;
using Locora.Application.Services;
using Locora.Infrastructure.Hosting;
using Locora.Infrastructure.Interprocess;
using Locora.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Locora.App;

public partial class App : Application
{
    private static IHost? _host;
    private static SingleInstanceLease? _singleInstanceLease;
    private Window? _mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static IHost Host => _host ?? throw new InvalidOperationException("The app host has not been initialized.");

    public static T GetService<T>()
        where T : notnull
    {
        return Host.Services.GetRequiredService<T>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceLease = SingleInstanceLease.Acquire("Locora.App", TimeSpan.FromSeconds(2));
        if (!_singleInstanceLease.OwnsMutex)
        {
            UserFacingAlerts.ShowDuplicateInstance("Locora", _singleInstanceLease.RootPath, _singleInstanceLease.TimedOut);
            _singleInstanceLease.Dispose();
            _singleInstanceLease = null;
            Exit();
            return;
        }

        _host = LocoraHostBuilder.BuildHost(
            "app",
            (_, services) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    services.AddSingleton<Services.ITrayService, Services.NotifyIconTrayService>();
                }
                else
                {
                    services.AddSingleton<Services.ITrayService, Services.NoOpTrayService>();
                }

                services.AddSingleton<Services.IProjectActionLauncher, Services.ProjectActionLauncher>();
                services.AddSingleton<Services.IClipboardService, Services.ClipboardService>();
                services.AddSingleton<Services.IDiagnosticReportService, Services.DiagnosticReportService>();
                services.AddSingleton<ISupervisorClient, NamedPipeSupervisorClient>();
                services.AddSingleton<IWorkbenchService, WorkbenchService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            });

        await _host.StartAsync();

        _mainWindow = GetService<MainWindow>();
        _mainWindow.Activated += OnMainWindowActivated;
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.Activate();
        GetService<Services.ITrayService>().Initialize(_mainWindow);
    }

    private void OnMainWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (sender is Window window)
        {
            window.Title = "Locora";
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_host is null)
        {
            _singleInstanceLease?.Dispose();
            _singleInstanceLease = null;
            return;
        }

        try
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        finally
        {
            _singleInstanceLease?.Dispose();
            _singleInstanceLease = null;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }
}
