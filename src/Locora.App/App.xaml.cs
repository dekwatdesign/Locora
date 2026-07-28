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

public partial class App : Microsoft.UI.Xaml.Application
{
    private static IHost? _host;
    private static SingleInstanceLease? _singleInstanceLease;
    private MainWindow? _mainWindow;
    private Services.ShellContextMenuActivationRelay? _activationRelay;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static IHost Host => _host ?? throw new InvalidOperationException("The app host has not been initialized.");

    public static MainWindow? CurrentMainWindow => (Current as App)?._mainWindow;

    public static T GetService<T>()
        where T : notnull
    {
        return Host.Services.GetRequiredService<T>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var rootPath = Environment.GetEnvironmentVariable("LOCORA_ROOT") ?? AppContext.BaseDirectory;
        var activationPipeName = Services.ShellContextMenuActivationRelay.CreatePipeName(rootPath);
        var shellCommand = Services.ShellContextMenuCommand.TryParse(Environment.GetCommandLineArgs().Skip(1).ToArray());

        _singleInstanceLease = SingleInstanceLease.Acquire("Locora.App", TimeSpan.FromSeconds(2));
        if (!_singleInstanceLease.OwnsMutex)
        {
            if (shellCommand is not null &&
                await Services.ShellContextMenuActivationRelay.TrySendAsync(activationPipeName, shellCommand, TimeSpan.FromSeconds(2)))
            {
                _singleInstanceLease.Dispose();
                _singleInstanceLease = null;
                Exit();
                return;
            }

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
                services.AddSingleton<Services.ITerminalSessionService, Services.ConPtyTerminalSessionService>();
                services.AddSingleton<Services.IClipboardService, Services.ClipboardService>();
                services.AddSingleton<Services.IDiagnosticReportService, Services.DiagnosticReportService>();
                services.AddSingleton<Services.IProjectPinStore, Services.ProjectPinStore>();
                services.AddSingleton<Services.IOnboardingStateStore, Services.OnboardingStateStore>();
                services.AddSingleton<Services.IShellContextMenuRegistrationService, Services.ShellContextMenuRegistrationService>();
                services.AddSingleton<Services.IUserEnvironmentChangeService, Services.UserEnvironmentChangeService>();
                services.AddSingleton<Services.IAppUpdateService, Services.AppUpdateService>();
                services.AddSingleton<Services.IPortableDistributionService, Services.PortableDistributionService>();
                services.AddSingleton<Services.IUserConfirmationService, Services.ContentDialogUserConfirmationService>();
                services.AddSingleton<Services.ILocoraStringResourceService, Services.LocoraStringResourceService>();
                services.AddSingleton<Services.LocoraLanguageService>();
                services.AddSingleton<ISupervisorClient, NamedPipeSupervisorClient>();
                services.AddSingleton<IWorkbenchService, WorkbenchService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            });

        await _host.StartAsync();

        GetService<Services.LocoraLanguageService>().ApplyConfiguredLanguage();

        _mainWindow = GetService<MainWindow>();
        _mainWindow.Activated += OnMainWindowActivated;
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.Activate();
        _activationRelay = new Services.ShellContextMenuActivationRelay(
            activationPipeName,
            DispatchShellContextMenuCommand);
        _activationRelay.Start();
        DispatchShellContextMenuCommand(shellCommand);
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
            _activationRelay?.Dispose();
            _activationRelay = null;
            _singleInstanceLease?.Dispose();
            _singleInstanceLease = null;
        }
    }

    private void DispatchShellContextMenuCommand(Services.ShellContextMenuCommand? command)
    {
        if (command is null || _mainWindow is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.DispatcherQueue.TryEnqueue(() => mainWindow.ViewModel.HandleShellContextMenuCommand(command));
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }
}
