using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Locora.App.Models;
using Locora.App.Services;
using Locora.Application.Abstractions;
using Locora.Domain.Entities;
using Locora.Domain.Enums;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IWorkbenchService _workbenchService;
    private readonly IProjectActionLauncher _projectActionLauncher;
    private readonly IClipboardService _clipboardService;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;
    private EnvironmentSnapshot? _lastSnapshot;
    private bool _initialized;
    private bool _isBusy;
    private bool _isSupervisorConnected;
    private bool _isSupervisorElevated;
    private int? _supervisorProcessId;
    private string _activeProfileName = "Bootstrap";
    private string _lastRefreshLabel = "Waiting for first snapshot";
    private string _sslSummary = "Local SSL has not been generated yet";
    private string _sslDetails = "Generate a local certificate authority to enable HTTPS vhosts.";
    private string _sslAuthorityPath = string.Empty;
    private string _sslCertificatesRoot = string.Empty;
    private string _sslTrustLabel = "Trust state unavailable";
    private string _sslProjectCertificateLabel = "0 project certificates";
    private string _sslLastGeneratedLabel = "Certificates have not been generated yet";
    private string _sslRepairLabel = "SSL repair state unavailable";
    private string _sslCaExpirationLabel = "CA expiration unavailable";
    private string _diagnosticReportPath = "No diagnostic report exported yet";
    private string _mailpitHeadline = "Mailpit is not configured";
    private string _mailpitSummary = "Add the Mailpit service to expose a local SMTP catcher and inbox.";
    private string _mailpitServiceStateLabel = "Mailpit state unavailable";
    private string _mailpitSmtpLabel = "SMTP 127.0.0.1:1025 / encryption none";
    private string _mailpitInboxUrl = "http://127.0.0.1:8025/";
    private string _mailpitApiUrl = "http://127.0.0.1:8025/api/v1/";
    private string _mailpitConnectionDetailsPath = string.Empty;
    private string _mailpitConnectionDetailsLabel = "Connection details file has not been generated yet";
    private bool _showMailpitIntegration;
    private bool _mailpitConnectionDetailsExists;
    private string _mailpitSmtpHost = "127.0.0.1";
    private int _mailpitSmtpPort = 1025;
    private string _mailpitSmtpEncryption = "none";
    private string _mailpitSmtpUsername = "(none)";
    private string _mailpitSmtpPassword = "(none)";

    public MainWindowViewModel(
        IWorkbenchService workbenchService,
        IProjectActionLauncher projectActionLauncher,
        IClipboardService clipboardService,
        IDiagnosticReportService diagnosticReportService,
        IEnvironmentPaths environmentPaths,
        IOptions<AppSettings> settings,
        ILogger<MainWindowViewModel> logger)
    {
        _workbenchService = workbenchService;
        _projectActionLauncher = projectActionLauncher;
        _clipboardService = clipboardService;
        _diagnosticReportService = diagnosticReportService;
        _environmentPaths = environmentPaths;
        _settings = settings.Value;
        _logger = logger;

        EnvironmentRoot = _environmentPaths.AppRoot;
        ProjectRoot = _environmentPaths.ProjectRoot;
        LogsRoot = _environmentPaths.LogsRoot;
        HostsPreviewPath = Path.Combine(_environmentPaths.ConfigRoot, "hosts.locora.generated");
        HostsBackupRoot = Path.Combine(_environmentPaths.ConfigRoot, "hosts.backups");
        WindowsHostsFilePath = ResolveWindowsHostsFilePath();
        PipeName = _settings.Supervisor.PipeName;
        PreferredToolchainSummary = $"{_settings.Experience.PreferredWebServer} / {_settings.Experience.PreferredDatabase} / {_settings.Experience.PreferredShell}";

        Services = new ObservableCollection<ServiceStatusCard>();
        Projects = new ObservableCollection<ProjectCard>();
        HealthIssues = new ObservableCollection<HealthIssueCard>();
        ValidationResults = new ObservableCollection<ValidationResultCard>();
        PortDiagnostics = new ObservableCollection<PortDiagnosticCard>();
        PermissionDiagnostics = new ObservableCollection<PermissionDiagnosticCard>();
        SslCertificateDiagnostics = new ObservableCollection<SslCertificateDiagnosticCard>();
        Activity = new ObservableCollection<TimelineEntry>();

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunAction);
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, CanRunAction);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, CanRunAction);
        StartServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StartServiceAsync, CanRunServiceAction);
        StopServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StopServiceAsync, CanRunServiceAction);
        RepairRuntimeCommand = new AsyncRelayCommand(RepairRuntimeAsync, CanRunAction);
        RepairServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(RepairServiceAsync, CanRepairServiceAction);
        RepairLocalSslCommand = new AsyncRelayCommand(RepairLocalSslAsync, CanRunAction);
        ApplyHostsPreviewCommand = new AsyncRelayCommand(ApplyHostsPreviewAsync, CanRunAction);
        RollbackHostsCommand = new AsyncRelayCommand(RollbackHostsAsync, CanRunAction);
        RestartSupervisorElevatedCommand = new AsyncRelayCommand(RestartSupervisorElevatedAsync, CanRunAction);
        ValidateNginxConfigCommand = new AsyncRelayCommand(ValidateNginxConfigAsync, CanRunAction);
        GenerateLocalSslCommand = new AsyncRelayCommand(GenerateLocalSslAsync, CanRunAction);
        TrustLocalSslCaCommand = new AsyncRelayCommand(TrustLocalSslCaAsync, CanRunAction);
        RollbackLocalSslTrustCommand = new AsyncRelayCommand(RollbackLocalSslTrustAsync, CanRunAction);
        CopyDiagnosticReportCommand = new RelayCommand(CopyDiagnosticReport, CanUseDiagnosticReport);
        ExportDiagnosticReportCommand = new AsyncRelayCommand(ExportDiagnosticReportAsync, CanUseDiagnosticReport);
        OpenMailpitInboxCommand = new RelayCommand(OpenMailpitInbox, CanUseMailpitIntegration);
        CopyMailpitSmtpConfigCommand = new RelayCommand(CopyMailpitSmtpConfig, CanUseMailpitIntegration);
        OpenMailpitConnectionDetailsCommand = new RelayCommand(OpenMailpitConnectionDetails, CanOpenMailpitConnectionDetails);
        OpenProjectUrlCommand = new RelayCommand<ProjectCard>(OpenProjectUrl, CanUseProject);
        OpenProjectFolderCommand = new RelayCommand<ProjectCard>(OpenProjectFolder, CanUseProject);
        OpenProjectTerminalCommand = new RelayCommand<ProjectCard>(OpenProjectTerminal, CanUseProject);
        OpenProjectEditorCommand = new RelayCommand<ProjectCard>(OpenProjectEditor, CanUseProject);
    }

    public ObservableCollection<ServiceStatusCard> Services { get; }

    public ObservableCollection<ProjectCard> Projects { get; }

    public ObservableCollection<HealthIssueCard> HealthIssues { get; }

    public ObservableCollection<ValidationResultCard> ValidationResults { get; }

    public ObservableCollection<PortDiagnosticCard> PortDiagnostics { get; }

    public ObservableCollection<PermissionDiagnosticCard> PermissionDiagnostics { get; }

    public ObservableCollection<SslCertificateDiagnosticCard> SslCertificateDiagnostics { get; }

    public ObservableCollection<TimelineEntry> Activity { get; }

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand StartAllCommand { get; }

    public IAsyncRelayCommand StopAllCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StartServiceCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StopServiceCommand { get; }

    public IAsyncRelayCommand RepairRuntimeCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> RepairServiceCommand { get; }

    public IAsyncRelayCommand RepairLocalSslCommand { get; }

    public IAsyncRelayCommand ApplyHostsPreviewCommand { get; }

    public IAsyncRelayCommand RollbackHostsCommand { get; }

    public IAsyncRelayCommand RestartSupervisorElevatedCommand { get; }

    public IAsyncRelayCommand ValidateNginxConfigCommand { get; }

    public IAsyncRelayCommand GenerateLocalSslCommand { get; }

    public IAsyncRelayCommand TrustLocalSslCaCommand { get; }

    public IAsyncRelayCommand RollbackLocalSslTrustCommand { get; }

    public IRelayCommand CopyDiagnosticReportCommand { get; }

    public IAsyncRelayCommand ExportDiagnosticReportCommand { get; }

    public IRelayCommand OpenMailpitInboxCommand { get; }

    public IRelayCommand CopyMailpitSmtpConfigCommand { get; }

    public IRelayCommand OpenMailpitConnectionDetailsCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectUrlCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectFolderCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectTerminalCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectEditorCommand { get; }

    public string EnvironmentRoot { get; }

    public string ProjectRoot { get; }

    public string LogsRoot { get; }

    public string HostsPreviewPath { get; }

    public string HostsBackupRoot { get; }

    public string WindowsHostsFilePath { get; }

    public string PipeName { get; }

    public string PreferredToolchainSummary { get; }

    public string DiagnosticReportPath
    {
        get => _diagnosticReportPath;
        private set => SetProperty(ref _diagnosticReportPath, value);
    }

    public string MailpitHeadline
    {
        get => _mailpitHeadline;
        private set => SetProperty(ref _mailpitHeadline, value);
    }

    public string MailpitSummary
    {
        get => _mailpitSummary;
        private set => SetProperty(ref _mailpitSummary, value);
    }

    public string MailpitServiceStateLabel
    {
        get => _mailpitServiceStateLabel;
        private set => SetProperty(ref _mailpitServiceStateLabel, value);
    }

    public string MailpitSmtpLabel
    {
        get => _mailpitSmtpLabel;
        private set => SetProperty(ref _mailpitSmtpLabel, value);
    }

    public string MailpitInboxUrl
    {
        get => _mailpitInboxUrl;
        private set => SetProperty(ref _mailpitInboxUrl, value);
    }

    public string MailpitApiUrl
    {
        get => _mailpitApiUrl;
        private set => SetProperty(ref _mailpitApiUrl, value);
    }

    public string MailpitConnectionDetailsPath
    {
        get => _mailpitConnectionDetailsPath;
        private set => SetProperty(ref _mailpitConnectionDetailsPath, value);
    }

    public string MailpitConnectionDetailsLabel
    {
        get => _mailpitConnectionDetailsLabel;
        private set => SetProperty(ref _mailpitConnectionDetailsLabel, value);
    }

    public bool ShowMailpitIntegration
    {
        get => _showMailpitIntegration;
        private set => SetProperty(ref _showMailpitIntegration, value);
    }

    public string SslSummary
    {
        get => _sslSummary;
        private set => SetProperty(ref _sslSummary, value);
    }

    public string SslDetails
    {
        get => _sslDetails;
        private set => SetProperty(ref _sslDetails, value);
    }

    public string SslAuthorityPath
    {
        get => _sslAuthorityPath;
        private set => SetProperty(ref _sslAuthorityPath, value);
    }

    public string SslCertificatesRoot
    {
        get => _sslCertificatesRoot;
        private set => SetProperty(ref _sslCertificatesRoot, value);
    }

    public string SslTrustLabel
    {
        get => _sslTrustLabel;
        private set => SetProperty(ref _sslTrustLabel, value);
    }

    public string SslProjectCertificateLabel
    {
        get => _sslProjectCertificateLabel;
        private set => SetProperty(ref _sslProjectCertificateLabel, value);
    }

    public string SslLastGeneratedLabel
    {
        get => _sslLastGeneratedLabel;
        private set => SetProperty(ref _sslLastGeneratedLabel, value);
    }

    public string SslRepairLabel
    {
        get => _sslRepairLabel;
        private set => SetProperty(ref _sslRepairLabel, value);
    }

    public string SslCaExpirationLabel
    {
        get => _sslCaExpirationLabel;
        private set => SetProperty(ref _sslCaExpirationLabel, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                StartAllCommand.NotifyCanExecuteChanged();
                StopAllCommand.NotifyCanExecuteChanged();
                StartServiceCommand.NotifyCanExecuteChanged();
                StopServiceCommand.NotifyCanExecuteChanged();
                RepairRuntimeCommand.NotifyCanExecuteChanged();
                RepairServiceCommand.NotifyCanExecuteChanged();
                RepairLocalSslCommand.NotifyCanExecuteChanged();
                ApplyHostsPreviewCommand.NotifyCanExecuteChanged();
                RollbackHostsCommand.NotifyCanExecuteChanged();
                RestartSupervisorElevatedCommand.NotifyCanExecuteChanged();
                ValidateNginxConfigCommand.NotifyCanExecuteChanged();
                GenerateLocalSslCommand.NotifyCanExecuteChanged();
                TrustLocalSslCaCommand.NotifyCanExecuteChanged();
                RollbackLocalSslTrustCommand.NotifyCanExecuteChanged();
                CopyDiagnosticReportCommand.NotifyCanExecuteChanged();
                ExportDiagnosticReportCommand.NotifyCanExecuteChanged();
                OpenMailpitInboxCommand.NotifyCanExecuteChanged();
                CopyMailpitSmtpConfigCommand.NotifyCanExecuteChanged();
                OpenMailpitConnectionDetailsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSupervisorConnected
    {
        get => _isSupervisorConnected;
        private set
        {
            if (SetProperty(ref _isSupervisorConnected, value))
            {
                OnPropertyChanged(nameof(ShowSupervisorWarning));
                OnPropertyChanged(nameof(ConnectionStatus));
                OnPropertyChanged(nameof(SupervisorPrivilegeLabel));
                OnPropertyChanged(nameof(SupervisorProcessLabel));
            }
        }
    }

    public bool ShowSupervisorWarning => !IsSupervisorConnected;

    public string ConnectionStatus => IsSupervisorConnected ? "Supervisor connected" : "Supervisor offline";

    public bool IsSupervisorElevated
    {
        get => _isSupervisorElevated;
        private set
        {
            if (SetProperty(ref _isSupervisorElevated, value))
            {
                OnPropertyChanged(nameof(SupervisorPrivilegeLabel));
            }
        }
    }

    public int? SupervisorProcessId
    {
        get => _supervisorProcessId;
        private set
        {
            if (SetProperty(ref _supervisorProcessId, value))
            {
                OnPropertyChanged(nameof(SupervisorProcessLabel));
            }
        }
    }

    public string SupervisorPrivilegeLabel => IsSupervisorConnected
        ? IsSupervisorElevated
            ? "Supervisor is running as administrator"
            : "Supervisor is not elevated"
        : "Supervisor privilege state unavailable";

    public string SupervisorProcessLabel => IsSupervisorConnected && SupervisorProcessId is int processId
        ? $"Supervisor PID {processId}"
        : "Supervisor PID unavailable";

    public string ActiveProfileName
    {
        get => _activeProfileName;
        private set => SetProperty(ref _activeProfileName, value);
    }

    public string LastRefreshLabel
    {
        get => _lastRefreshLabel;
        private set => SetProperty(ref _lastRefreshLabel, value);
    }

    public int RunningServicesCount => Services.Count(service => string.Equals(service.StateLabel, "Running", StringComparison.OrdinalIgnoreCase));

    public int TotalServicesCount => Services.Count;

    public int HealthIssueCount => HealthIssues.Count;

    public int InvalidValidationCount => ValidationResults.Count(validation => string.Equals(validation.State, "Invalid", StringComparison.OrdinalIgnoreCase));

    public int PortAttentionCount => PortDiagnostics.Count(diagnostic => !string.Equals(diagnostic.StateLabel, "Ready", StringComparison.OrdinalIgnoreCase));

    public int PermissionAttentionCount => PermissionDiagnostics.Count(diagnostic => !string.Equals(diagnostic.StateLabel, "Ready", StringComparison.OrdinalIgnoreCase));

    public int SslCertificateAttentionCount => SslCertificateDiagnostics.Count(diagnostic => !string.Equals(diagnostic.StateLabel, "Ready", StringComparison.OrdinalIgnoreCase));

    public string DiagnosticsHeadline
    {
        get
        {
            var totalAttention = HealthIssueCount + InvalidValidationCount + PortAttentionCount + PermissionAttentionCount + SslCertificateAttentionCount;
            return totalAttention == 0
                ? "Diagnostics are clear"
                : $"{totalAttention} diagnostic items need attention";
        }
    }

    public string DiagnosticsSummary
    {
        get
        {
            var healthLabel = HealthIssueCount == 1 ? "health issue" : "health issues";
            var validationLabel = InvalidValidationCount == 1 ? "config check" : "config checks";
            var portLabel = PortAttentionCount == 1 ? "port check" : "port checks";
            var permissionLabel = PermissionAttentionCount == 1 ? "permission check" : "permission checks";
            var sslLabel = SslCertificateAttentionCount == 1 ? "SSL certificate" : "SSL certificates";

            return $"{HealthIssueCount} {healthLabel}, {InvalidValidationCount} failing {validationLabel}, {PortAttentionCount} {portLabel} needing attention, {PermissionAttentionCount} {permissionLabel} needing attention, {SslCertificateAttentionCount} {sslLabel} needing repair.";
        }
    }

    public string ConfigValidationSummary
    {
        get
        {
            if (ValidationResults.Count == 0)
            {
                return "No config validation results have been reported yet.";
            }

            var validCount = ValidationResults.Count - InvalidValidationCount;
            return $"{validCount}/{ValidationResults.Count} config checks passing";
        }
    }

    public string PortDiagnosticsSummary
    {
        get
        {
            if (PortDiagnostics.Count == 0)
            {
                return "Port diagnostics unavailable";
            }

            var readyCount = PortDiagnostics.Count - PortAttentionCount;
            return PortAttentionCount == 0
                ? $"{readyCount}/{PortDiagnostics.Count} port checks ready"
                : $"{readyCount}/{PortDiagnostics.Count} port checks ready, {PortAttentionCount} need attention";
        }
    }

    public string PermissionDiagnosticsSummary
    {
        get
        {
            if (PermissionDiagnostics.Count == 0)
            {
                return "Permission diagnostics unavailable";
            }

            var readyCount = PermissionDiagnostics.Count(diagnostic => string.Equals(diagnostic.StateLabel, "Ready", StringComparison.OrdinalIgnoreCase));
            var attentionCount = PermissionDiagnostics.Count - readyCount;

            return attentionCount == 0
                ? $"{readyCount}/{PermissionDiagnostics.Count} permission checks ready"
                : $"{readyCount}/{PermissionDiagnostics.Count} permission checks ready, {attentionCount} need attention";
        }
    }

    public string HostsAccessDiagnosticLabel => FormatPermissionDiagnosticLabel(
        "hosts_write_path",
        "Hosts file update path",
        "Hosts permission diagnostics are not available yet.");

    public string ElevationRestartDiagnosticLabel => FormatPermissionDiagnosticLabel(
        "elevation_restart",
        "Restart-as-admin flow",
        "Elevation restart diagnostics are not available yet.");

    public string WorkspaceWriteAccessDiagnosticLabel => FormatPermissionDiagnosticLabel(
        "portable_workspace_access",
        "Portable workspace write access",
        "Workspace permission diagnostics are not available yet.");

    public string SslTrustAccessDiagnosticLabel => FormatPermissionDiagnosticLabel(
        "current_user_ssl_trust",
        "Current-user SSL trust access",
        "SSL trust permission diagnostics are not available yet.");

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        RecordActivity("Info", "Locora shell initialized.");
        await RefreshAsync();
    }

    private bool CanRunAction() => !IsBusy;

    private bool CanRunServiceAction(ServiceStatusCard? service) => !IsBusy && service is not null;

    private bool CanUseDiagnosticReport() => !IsBusy && _lastSnapshot is not null;

    private bool CanUseMailpitIntegration() => !IsBusy && ShowMailpitIntegration;

    private bool CanOpenMailpitConnectionDetails() => !IsBusy && _mailpitConnectionDetailsExists;

    private bool CanRepairServiceAction(ServiceStatusCard? service)
    {
        if (IsBusy || service is null)
        {
            return false;
        }

        return service.Key.Equals("nginx", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("mariadb", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("redis", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("mailpit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUseProject(ProjectCard? project) => project is not null;

    private async Task RefreshAsync()
    {
        await ExecuteAsync(
            "Refreshing environment snapshot",
            () => _workbenchService.GetSnapshotAsync());
    }

    private async Task StartAllAsync()
    {
        await ExecuteAsync(
            "Start all requested from dashboard",
            () => _workbenchService.StartAllAsync());
    }

    private async Task StopAllAsync()
    {
        await ExecuteAsync(
            "Stop all requested from dashboard",
            () => _workbenchService.StopAllAsync());
    }

    private async Task StartServiceAsync(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        await ExecuteAsync(
            $"Start requested for {service.Name}",
            () => _workbenchService.StartServiceAsync(service.Key));
    }

    private async Task StopServiceAsync(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        await ExecuteAsync(
            $"Stop requested for {service.Name}",
            () => _workbenchService.StopServiceAsync(service.Key));
    }

    private async Task RepairRuntimeAsync()
    {
        await ExecuteAsync(
            "Repair runtime configs requested",
            () => _workbenchService.RepairRuntimeAsync());
    }

    private async Task RepairServiceAsync(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        await ExecuteAsync(
            $"Repair requested for {service.Name}",
            () => _workbenchService.RepairServiceAsync(service.Key));
    }

    private async Task RepairLocalSslAsync()
    {
        await ExecuteAsync(
            "Local SSL repair requested",
            () => _workbenchService.RepairLocalSslAsync());
    }

    private async Task ApplyHostsPreviewAsync()
    {
        await ExecuteAsync(
            "Apply hosts preview requested",
            () => _workbenchService.ApplyHostsPreviewAsync());
    }

    private async Task RollbackHostsAsync()
    {
        await ExecuteAsync(
            "Hosts rollback requested",
            () => _workbenchService.RollbackHostsAsync());
    }

    private async Task RestartSupervisorElevatedAsync()
    {
        await ExecuteAsync(
            "Elevated supervisor restart requested",
            () => _workbenchService.RestartSupervisorElevatedAsync());
    }

    private async Task ValidateNginxConfigAsync()
    {
        await ExecuteAsync(
            "Nginx config validation requested",
            () => _workbenchService.ValidateNginxConfigAsync());
    }

    private async Task GenerateLocalSslAsync()
    {
        await ExecuteAsync(
            "Local SSL generation requested",
            () => _workbenchService.GenerateLocalSslAsync());
    }

    private async Task TrustLocalSslCaAsync()
    {
        await ExecuteAsync(
            "Trust local SSL certificate authority requested",
            () => _workbenchService.TrustLocalSslCaAsync());
    }

    private async Task RollbackLocalSslTrustAsync()
    {
        await ExecuteAsync(
            "Local SSL trust rollback requested",
            () => _workbenchService.RollbackLocalSslTrustAsync());
    }

    private void CopyDiagnosticReport()
    {
        if (_lastSnapshot is null)
        {
            return;
        }

        try
        {
            _diagnosticReportService.CopyToClipboard(_lastSnapshot);
            RecordActivity("Info", "Diagnostic report copied to clipboard.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to copy diagnostic report.");
            RecordActivity("Error", $"Diagnostic report copy failed: {exception.Message}");
        }
    }

    private async Task ExportDiagnosticReportAsync()
    {
        if (IsBusy || _lastSnapshot is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            RecordActivity("Info", "Exporting diagnostic report.");
            DiagnosticReportPath = await _diagnosticReportService.ExportReportAsync(_lastSnapshot);
            RecordActivity("Info", $"Diagnostic report exported: {DiagnosticReportPath}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to export diagnostic report.");
            RecordActivity("Error", $"Diagnostic report export failed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenMailpitInbox()
    {
        ExecuteDesktopAction(
            "Mailpit inbox opened.",
            "Mailpit inbox launch failed.",
            () => _projectActionLauncher.OpenUrl(MailpitInboxUrl));
    }

    private void CopyMailpitSmtpConfig()
    {
        ExecuteDesktopAction(
            "Mailpit SMTP config copied to clipboard.",
            "Mailpit SMTP config copy failed.",
            () => _clipboardService.CopyText(BuildMailpitSmtpConfigSnippet()));
    }

    private void OpenMailpitConnectionDetails()
    {
        ExecuteDesktopAction(
            "Mailpit connection details opened.",
            "Mailpit connection details could not be opened.",
            () => _projectActionLauncher.OpenFile(MailpitConnectionDetailsPath));
    }

    private void OpenProjectUrl(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project URL", card => _projectActionLauncher.OpenUrl(card.Url));
    }

    private void OpenProjectFolder(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project folder", card => _projectActionLauncher.OpenFolder(card.Folder));
    }

    private void OpenProjectTerminal(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project terminal", card => _projectActionLauncher.OpenTerminal(card.Folder));
    }

    private void OpenProjectEditor(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project editor", card => _projectActionLauncher.OpenEditor(card.Folder));
    }

    private async Task ExecuteAsync(string activityMessage, Func<Task<EnvironmentSnapshot>> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            RecordActivity("Info", activityMessage);

            var snapshot = await action();
            ApplySnapshot(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Workbench action failed.");
            RecordActivity("Error", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(EnvironmentSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        ActiveProfileName = snapshot.ActiveProfile;
        LastRefreshLabel = $"Updated {snapshot.GeneratedAt.LocalDateTime:g}";
        IsSupervisorConnected = snapshot.IsSupervisorReachable;
        IsSupervisorElevated = snapshot.IsSupervisorElevated;
        SupervisorProcessId = snapshot.SupervisorProcessId;

        ReplaceCollection(
            Services,
            snapshot.Services.Select(service => new ServiceStatusCard(
                service.Key,
                service.DisplayName,
                service.Version,
                service.Port is null ? "No port" : $"Port {service.Port}",
                service.State.ToString(),
                service.Note ?? "No notes yet",
                service.AutoStart)));

        ReplaceCollection(
            Projects,
            snapshot.Projects.Select(project => new ProjectCard(
                project.Name,
                project.Url,
                project.Runtime,
                project.Path,
                OpenProjectUrlCommand,
                OpenProjectFolderCommand,
                OpenProjectTerminalCommand,
                OpenProjectEditorCommand)));

        ReplaceCollection(
            HealthIssues,
            snapshot.Issues.Select(issue => new HealthIssueCard(
                issue.Severity.ToString(),
                issue.Title,
                issue.Description,
                issue.SuggestedAction)));

        ReplaceCollection(
            ValidationResults,
            snapshot.ValidationResults.Select(validation => new ValidationResultCard(
                validation.DisplayName,
                validation.IsValid ? "Valid" : "Invalid",
                validation.Summary,
                validation.Details,
                validation.CheckedAt.LocalDateTime.ToString("g"))));

        ReplaceCollection(
            PortDiagnostics,
            snapshot.PortDiagnostics.Select(diagnostic => new PortDiagnosticCard(
                diagnostic.Key,
                diagnostic.DisplayName,
                diagnostic.Port == 0 ? "Port unavailable" : $"Port {diagnostic.Port}",
                diagnostic.State.ToString(),
                diagnostic.Summary,
                diagnostic.Details,
                diagnostic.SuggestedAction,
                FormatPortOwnerLabel(diagnostic.OwnerProcessId, diagnostic.OwnerProcessName))));

        ReplaceCollection(
            PermissionDiagnostics,
            snapshot.PermissionDiagnostics.Select(diagnostic => new PermissionDiagnosticCard(
                diagnostic.Key,
                diagnostic.DisplayName,
                diagnostic.State.ToString(),
                diagnostic.Summary,
                diagnostic.Details,
                diagnostic.SuggestedAction)));

        ReplaceCollection(
            SslCertificateDiagnostics,
            snapshot.SslStatus.CertificateDiagnostics.Select(diagnostic => new SslCertificateDiagnosticCard(
                diagnostic.Key,
                diagnostic.ProjectName,
                diagnostic.Host,
                diagnostic.State.ToString(),
                diagnostic.Reason,
                diagnostic.Summary,
                diagnostic.Details,
                diagnostic.SuggestedAction,
                diagnostic.CertificatePath,
                diagnostic.ExpiresAt is { } expiresAt ? $"Expires {expiresAt.LocalDateTime:g}" : "Expiration unavailable")));

        SslSummary = snapshot.SslStatus.Summary;
        SslDetails = snapshot.SslStatus.Details;
        SslAuthorityPath = snapshot.SslStatus.CaCertificatePath;
        SslCertificatesRoot = snapshot.SslStatus.CertificatesRoot;
        SslTrustLabel = snapshot.SslStatus.TrustSupported
            ? snapshot.SslStatus.IsCurrentUserTrusted
                ? "Current user trust: enabled"
                : "Current user trust: not enabled"
            : "Trust-state checks are only available on Windows";
        SslProjectCertificateLabel = $"{snapshot.SslStatus.ProjectCertificateCount} project certificates / {snapshot.SslStatus.HttpsProjectCount} HTTPS projects";
        SslLastGeneratedLabel = snapshot.SslStatus.LastGeneratedAt is { } lastGeneratedAt
            ? $"Last generated {lastGeneratedAt.LocalDateTime:g}"
            : "Certificates have not been generated yet";
        SslRepairLabel = CreateSslRepairLabel(snapshot.SslStatus);
        SslCaExpirationLabel = snapshot.SslStatus.CaExpiresAt is { } caExpiresAt
            ? $"CA expires {caExpiresAt.LocalDateTime:g}"
            : "CA expiration unavailable";
        ApplyMailpitIntegration(snapshot);

        OnPropertyChanged(nameof(RunningServicesCount));
        OnPropertyChanged(nameof(TotalServicesCount));
        OnPropertyChanged(nameof(HealthIssueCount));
        OnPropertyChanged(nameof(InvalidValidationCount));
        OnPropertyChanged(nameof(PortAttentionCount));
        OnPropertyChanged(nameof(PermissionAttentionCount));
        OnPropertyChanged(nameof(SslCertificateAttentionCount));
        OnPropertyChanged(nameof(DiagnosticsHeadline));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        OnPropertyChanged(nameof(ConfigValidationSummary));
        OnPropertyChanged(nameof(PortDiagnosticsSummary));
        OnPropertyChanged(nameof(PermissionDiagnosticsSummary));
        OnPropertyChanged(nameof(HostsAccessDiagnosticLabel));
        OnPropertyChanged(nameof(ElevationRestartDiagnosticLabel));
        OnPropertyChanged(nameof(WorkspaceWriteAccessDiagnosticLabel));
        OnPropertyChanged(nameof(SslTrustAccessDiagnosticLabel));
        CopyDiagnosticReportCommand.NotifyCanExecuteChanged();
        ExportDiagnosticReportCommand.NotifyCanExecuteChanged();
        OpenMailpitInboxCommand.NotifyCanExecuteChanged();
        CopyMailpitSmtpConfigCommand.NotifyCanExecuteChanged();
        OpenMailpitConnectionDetailsCommand.NotifyCanExecuteChanged();

        RecordActivity(
            snapshot.IsSupervisorReachable ? "Info" : "Warning",
            $"Snapshot applied with {RunningServicesCount}/{TotalServicesCount} services active.");
    }

    private void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ExecuteProjectAction(ProjectCard? project, string level, string activityPrefix, Action<ProjectCard> action)
    {
        if (project is null)
        {
            return;
        }

        try
        {
            action(project);
            RecordActivity(level, $"{activityPrefix}: {project.Name}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Project action failed for {Project}", project.Name);
            RecordActivity("Error", $"{project.Name}: {exception.Message}");
        }
    }

    private void ExecuteDesktopAction(string successMessage, string failureMessage, Action action)
    {
        try
        {
            action();
            RecordActivity("Info", successMessage);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Message}", failureMessage);
            RecordActivity("Error", $"{failureMessage} {exception.Message}");
        }
    }

    private void RecordActivity(string level, string message)
    {
        Activity.Insert(0, new TimelineEntry(DateTimeOffset.Now.ToString("HH:mm:ss"), level, message));

        while (Activity.Count > 50)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private string FormatPermissionDiagnosticLabel(string key, string nameFallback, string summaryFallback)
    {
        var diagnostic = PermissionDiagnostics.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return diagnostic is null
            ? $"{nameFallback}: {summaryFallback}"
            : $"{diagnostic.Name}: {diagnostic.Summary}";
    }

    private static string FormatPortOwnerLabel(int? ownerProcessId, string? ownerProcessName)
    {
        if (ownerProcessId is null)
        {
            return "No owner detected";
        }

        return string.IsNullOrWhiteSpace(ownerProcessName)
            ? $"PID {ownerProcessId}"
            : $"{ownerProcessName} (PID {ownerProcessId})";
    }

    private static string CreateSslRepairLabel(SslStatus sslStatus)
    {
        var repairCount = sslStatus.MissingProjectCertificateCount +
            sslStatus.ExpiredProjectCertificateCount +
            sslStatus.ExpiringProjectCertificateCount;

        return repairCount == 0
            ? "SSL repair not needed"
            : $"{repairCount} SSL certificate checks need repair or renewal";
    }

    private void ApplyMailpitIntegration(EnvironmentSnapshot snapshot)
    {
        var mailpitService = snapshot.Services.FirstOrDefault(service => service.Key.Equals("mailpit", StringComparison.OrdinalIgnoreCase));
        var details = LoadMailpitConnectionDetails(mailpitService);

        ShowMailpitIntegration = mailpitService is not null || details.ConnectionDetailsExists;
        MailpitInboxUrl = details.InboxUrl;
        MailpitApiUrl = details.ApiUrl;
        MailpitSmtpLabel = $"SMTP {details.SmtpHost}:{details.SmtpPort} / encryption {details.Encryption}";
        MailpitConnectionDetailsPath = details.ConnectionDetailsPath;
        _mailpitConnectionDetailsExists = details.ConnectionDetailsExists;
        _mailpitSmtpHost = details.SmtpHost;
        _mailpitSmtpPort = details.SmtpPort;
        _mailpitSmtpEncryption = details.Encryption;
        _mailpitSmtpUsername = details.Username;
        _mailpitSmtpPassword = details.Password;
        MailpitConnectionDetailsLabel = details.ConnectionDetailsExists
            ? "Generated connection details are ready to open."
            : "Run runtime repair or start Mailpit once to generate a connection details file.";

        if (mailpitService is null)
        {
            MailpitHeadline = "Mailpit is not configured";
            MailpitServiceStateLabel = "Service definition unavailable";
            MailpitSummary = "Locora can surface a local inbox and SMTP snippet as soon as the Mailpit service is enabled.";
            return;
        }

        MailpitServiceStateLabel = $"Service state: {mailpitService.State}";
        MailpitHeadline = mailpitService.State == ServiceState.Running
            ? "Mailpit inbox is ready"
            : mailpitService.State == ServiceState.Starting
                ? "Mailpit is starting"
                : "Mailpit inbox is available on demand";

        var statusLead = mailpitService.State == ServiceState.Running
            ? "Open the live inbox to inspect captured messages."
            : "The inbox URL and SMTP settings are ready even before the service is started.";

        MailpitSummary = $"{statusLead} Send mail to {details.SmtpHost}:{details.SmtpPort} with no auth, then inspect messages at {details.InboxUrl}";
    }

    private MailpitConnectionDetails LoadMailpitConnectionDetails(ServiceDescriptor? mailpitService)
    {
        var connectionDetailsPath = Path.Combine(_environmentPaths.ConfigRoot, "mailpit", "connection-details.md");
        var fallbackSmtpPort = mailpitService?.Port ?? 1025;
        var connectionDetailsExists = File.Exists(connectionDetailsPath);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (connectionDetailsExists)
        {
            foreach (var line in File.ReadLines(connectionDetailsPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }
        }

        var inboxUrl = EnsureTrailingSlash(
            values.TryGetValue("Web UI", out var webUi) && !string.IsNullOrWhiteSpace(webUi)
                ? webUi
                : "http://127.0.0.1:8025/");

        var apiUrl = EnsureTrailingSlash(
            values.TryGetValue("API", out var api) && !string.IsNullOrWhiteSpace(api)
                ? api
                : $"{inboxUrl.TrimEnd('/')}/api/v1/");

        return new MailpitConnectionDetails(
            values.TryGetValue("SMTP host", out var smtpHost) && !string.IsNullOrWhiteSpace(smtpHost) ? smtpHost : "127.0.0.1",
            TryParsePort(values.TryGetValue("SMTP port", out var smtpPortText) ? smtpPortText : null, fallbackSmtpPort),
            values.TryGetValue("Encryption", out var encryption) && !string.IsNullOrWhiteSpace(encryption) ? encryption : "none",
            values.TryGetValue("Username", out var username) && !string.IsNullOrWhiteSpace(username) ? username : "(none)",
            values.TryGetValue("Password", out var password) && !string.IsNullOrWhiteSpace(password) ? password : "(none)",
            inboxUrl,
            apiUrl,
            connectionDetailsPath,
            connectionDetailsExists);
    }

    private string BuildMailpitSmtpConfigSnippet()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SMTP_HOST={_mailpitSmtpHost}");
        builder.AppendLine($"SMTP_PORT={_mailpitSmtpPort}");
        builder.AppendLine(_mailpitSmtpUsername.Equals("(none)", StringComparison.OrdinalIgnoreCase) ? "SMTP_USERNAME=" : $"SMTP_USERNAME={_mailpitSmtpUsername}");
        builder.AppendLine(_mailpitSmtpPassword.Equals("(none)", StringComparison.OrdinalIgnoreCase) ? "SMTP_PASSWORD=" : $"SMTP_PASSWORD={_mailpitSmtpPassword}");
        builder.AppendLine(_mailpitSmtpEncryption.Equals("none", StringComparison.OrdinalIgnoreCase) ? "SMTP_ENCRYPTION=" : $"SMTP_ENCRYPTION={_mailpitSmtpEncryption}");
        builder.AppendLine($"MAILPIT_WEB_UI={MailpitInboxUrl}");
        builder.AppendLine($"MAILPIT_API={MailpitApiUrl}");
        return builder.ToString();
    }

    private static string EnsureTrailingSlash(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.EndsWith('/', StringComparison.Ordinal)
            ? value
            : $"{value}/";
    }

    private static int TryParsePort(string? text, int fallback)
    {
        return int.TryParse(text, out var port) && port > 0
            ? port
            : fallback;
    }

    private sealed record MailpitConnectionDetails(
        string SmtpHost,
        int SmtpPort,
        string Encryption,
        string Username,
        string Password,
        string InboxUrl,
        string ApiUrl,
        string ConnectionDetailsPath,
        bool ConnectionDetailsExists);

    private static string ResolveWindowsHostsFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LOCORA_HOSTS_FILE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(systemDirectory))
        {
            return Path.Combine(systemDirectory, "drivers", "etc", "hosts");
        }

        return Path.Combine(
            Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
            "System32",
            "drivers",
            "etc",
            "hosts");
    }
}
