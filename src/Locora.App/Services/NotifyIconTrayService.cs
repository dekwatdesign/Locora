using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Locora.App;
using Locora.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using FormsToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace Locora.App.Services;

public sealed partial class NotifyIconTrayService : ITrayService, IDisposable
{
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const int MaxTooltipLength = 63;
    private readonly MainWindowViewModel _viewModel;
    private readonly ILogger<NotifyIconTrayService> _logger;
    private FormsNotifyIcon? _notifyIcon;
    private FormsContextMenuStrip? _contextMenu;
    private FormsToolStripMenuItem? _openMenuItem;
    private FormsToolStripMenuItem? _hideMenuItem;
    private FormsToolStripMenuItem? _diagnosticsMenuItem;
    private FormsToolStripMenuItem? _refreshMenuItem;
    private FormsToolStripMenuItem? _startAllMenuItem;
    private FormsToolStripMenuItem? _stopAllMenuItem;
    private Window? _window;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcherQueue;
    private AppWindow? _appWindow;
    private IntPtr _windowHandle;
    private bool _initialized;
    private bool _isDisposed;
    private bool _isExitRequested;
    private bool _isWindowVisible = true;
    private bool _hasShownHideHint;

    public NotifyIconTrayService(
        MainWindowViewModel viewModel,
        ILogger<NotifyIconTrayService> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void Initialize(Window window)
    {
        if (_initialized || _isDisposed)
        {
            return;
        }

        try
        {
            _window = window;
            _mainWindow = window as MainWindow;
            _dispatcherQueue = window.DispatcherQueue;
            _windowHandle = WindowNative.GetWindowHandle(window);
            _appWindow = window.AppWindow;
            _appWindow.Closing += OnAppWindowClosing;
            _isWindowVisible = true;

            CreateNotifyIcon();
            UpdateTooltip();
            UpdateMenuState();
            _initialized = true;
            _logger.LogInformation("Tray integration initialized.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Tray initialization failed. Locora will continue without tray quick actions.");
        }
    }

    public void ShowStatus(string message)
    {
        if (_notifyIcon is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Enqueue(() =>
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Text = ClipTooltip($"Locora: {message}");
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;
    }

    private void CreateNotifyIcon()
    {
        _openMenuItem = new FormsToolStripMenuItem("Open Locora", null, (_, _) => RestoreMainWindow());
        _hideMenuItem = new FormsToolStripMenuItem("Hide Window", null, (_, _) => HideToTray(showHint: false));
        _diagnosticsMenuItem = new FormsToolStripMenuItem("Open Diagnostics", null, (_, _) => OpenDiagnostics());
        _refreshMenuItem = new FormsToolStripMenuItem("Refresh Status", null, async (_, _) => await ExecuteCommandAsync(_viewModel.RefreshCommand, "Refreshing status"));
        _startAllMenuItem = new FormsToolStripMenuItem("Start All", null, async (_, _) => await ExecuteCommandAsync(_viewModel.StartAllCommand, "Starting all services"));
        _stopAllMenuItem = new FormsToolStripMenuItem("Stop All", null, async (_, _) => await ExecuteCommandAsync(_viewModel.StopAllCommand, "Stopping all services"));
        var exitMenuItem = new FormsToolStripMenuItem("Exit Locora", null, (_, _) => ExitApplication());

        _contextMenu = new FormsContextMenuStrip
        {
            ShowImageMargin = false
        };

        _contextMenu.Opening += (_, _) => UpdateMenuState();
        _contextMenu.Items.AddRange(
        [
            _openMenuItem,
            _hideMenuItem,
            _diagnosticsMenuItem,
            new System.Windows.Forms.ToolStripSeparator(),
            _refreshMenuItem,
            _startAllMenuItem,
            _stopAllMenuItem,
            new System.Windows.Forms.ToolStripSeparator(),
            exitMenuItem
        ]);

        _notifyIcon = new FormsNotifyIcon
        {
            Icon = (Icon)SystemIcons.Application.Clone(),
            Text = ClipTooltip("Locora: starting up"),
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreMainWindow();
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    private void OnNotifyIconMouseClick(object? sender, FormsMouseEventArgs args)
    {
        if (args.Button == FormsMouseButtons.Left)
        {
            RestoreMainWindow();
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExitRequested)
        {
            return;
        }

        args.Cancel = true;
        HideToTray(showHint: true);
    }

    private async Task ExecuteCommandAsync(IAsyncRelayCommand command, string statusMessage)
    {
        if (_isDisposed || !command.CanExecute(null))
        {
            Enqueue(UpdateMenuState);
            return;
        }

        try
        {
            ShowStatus(statusMessage);
            UpdateMenuState();
            await command.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Tray command failed: {StatusMessage}", statusMessage);
            ShowBalloonTip("Tray action failed", exception.Message, FormsToolTipIcon.Warning);
        }
        finally
        {
            Enqueue(() =>
            {
                UpdateTooltip();
                UpdateMenuState();
            });
        }
    }

    private void RestoreMainWindow()
    {
        Enqueue(RestoreMainWindowCore);
    }

    private void RestoreMainWindowCore()
    {
        if (_window is null)
        {
            return;
        }

        _appWindow?.Show();

        if (_windowHandle != IntPtr.Zero)
        {
            ShowWindow(_windowHandle, SwRestore);
            SetForegroundWindow(_windowHandle);
        }

        _window.Activate();
        _isWindowVisible = true;
        UpdateMenuState();
        UpdateTooltip();
    }

    private void OpenDiagnostics()
    {
        Enqueue(() =>
        {
            RestoreMainWindowCore();
            _mainWindow?.NavigateTo("diagnostics");
        });
    }

    private void HideToTray(bool showHint)
    {
        Enqueue(() =>
        {
            if (_window is null || !_isWindowVisible)
            {
                return;
            }

            _appWindow?.Hide();

            if (_windowHandle != IntPtr.Zero)
            {
                ShowWindow(_windowHandle, SwHide);
            }

            _isWindowVisible = false;
            UpdateMenuState();
            UpdateTooltip();

            if (showHint && !_hasShownHideHint)
            {
                _hasShownHideHint = true;
                ShowBalloonTip(
                    "Locora is still running",
                    "The window is hidden to the notification area. Use the tray icon to reopen, manage services, or exit.",
                    FormsToolTipIcon.Info);
            }
        });
    }

    private void ExitApplication()
    {
        Enqueue(() =>
        {
            if (_window is null)
            {
                return;
            }

            _isExitRequested = true;
            _window.Close();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_isDisposed)
        {
            return;
        }

        if (args.PropertyName is nameof(MainWindowViewModel.IsBusy))
        {
            Enqueue(UpdateMenuState);
            return;
        }

        if (args.PropertyName is nameof(MainWindowViewModel.IsSupervisorConnected) or
            nameof(MainWindowViewModel.RunningServicesCount) or
            nameof(MainWindowViewModel.TotalServicesCount) or
            nameof(MainWindowViewModel.HealthIssueCount) or
            nameof(MainWindowViewModel.InvalidValidationCount) or
            nameof(MainWindowViewModel.PortAttentionCount) or
            nameof(MainWindowViewModel.PermissionAttentionCount) or
            nameof(MainWindowViewModel.SslCertificateAttentionCount))
        {
            Enqueue(() =>
            {
                UpdateTooltip();
                UpdateMenuState();
            });
        }
    }

    private void UpdateTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = ClipTooltip(BuildStatusMessage());
    }

    private void UpdateMenuState()
    {
        if (_contextMenu is null)
        {
            return;
        }

        var canRefresh = _viewModel.RefreshCommand.CanExecute(null);
        var canStartAll = _viewModel.StartAllCommand.CanExecute(null);
        var canStopAll = _viewModel.StopAllCommand.CanExecute(null);

        if (_openMenuItem is not null)
        {
            _openMenuItem.Enabled = true;
        }

        if (_hideMenuItem is not null)
        {
            _hideMenuItem.Enabled = _isWindowVisible;
        }

        if (_diagnosticsMenuItem is not null)
        {
            _diagnosticsMenuItem.Enabled = true;
        }

        if (_refreshMenuItem is not null)
        {
            _refreshMenuItem.Enabled = canRefresh;
        }

        if (_startAllMenuItem is not null)
        {
            _startAllMenuItem.Enabled = canStartAll;
        }

        if (_stopAllMenuItem is not null)
        {
            _stopAllMenuItem.Enabled = canStopAll;
        }
    }

    private string BuildStatusMessage()
    {
        if (!_viewModel.IsSupervisorConnected)
        {
            return "Locora: supervisor offline";
        }

        var attentionCount = _viewModel.HealthIssueCount +
            _viewModel.InvalidValidationCount +
            _viewModel.PortAttentionCount +
            _viewModel.PermissionAttentionCount +
            _viewModel.SslCertificateAttentionCount;

        return attentionCount == 0
            ? $"Locora: {_viewModel.RunningServicesCount}/{_viewModel.TotalServicesCount} services running"
            : $"Locora: {_viewModel.RunningServicesCount}/{_viewModel.TotalServicesCount} running, {attentionCount} alerts";
    }

    private void ShowBalloonTip(string title, string message, FormsToolTipIcon icon)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        try
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                Clip(title, 63),
                Clip(message.ReplaceLineEndings(" "), 255),
                icon);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Tray balloon tip could not be shown.");
        }
    }

    private void Enqueue(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private static string ClipTooltip(string value)
    {
        return Clip(value.ReplaceLineEndings(" "), MaxTooltipLength);
    }

    private static string Clip(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
