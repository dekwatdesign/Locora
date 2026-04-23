using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const string DefaultGeneratedDomainSuffix = "locora.test";
    private const string DefaultGeneratedDomainScheme = "http";
    private static readonly JsonSerializerOptions ProjectSettingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
    private static readonly string[] DefaultIndexFileNames = ["index.php", "index.html", "index.htm"];
    private static readonly string[] DefaultIgnoredDirectoryNames = [".git", ".idea", ".vscode", "node_modules", "vendor"];
    private static readonly string[] GeneratedDomainSchemes = ["http", "https"];
    private readonly IWorkbenchService _workbenchService;
    private readonly IProjectActionLauncher _projectActionLauncher;
    private readonly ITerminalSessionService _terminalSessionService;
    private readonly IClipboardService _clipboardService;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IProjectPinStore _projectPinStore;
    private readonly IOnboardingStateStore _onboardingStateStore;
    private readonly IShellContextMenuRegistrationService _shellContextMenuRegistrationService;
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;
    private EnvironmentSnapshot? _lastSnapshot;
    private HashSet<string> _pinnedProjectKeys = new(StringComparer.OrdinalIgnoreCase);
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
    private string _generatedDomainSuffix = DefaultGeneratedDomainSuffix;
    private string _generatedDomainScheme = DefaultGeneratedDomainScheme;
    private string _domainSettingsStatus = $"Generated URLs use {DefaultGeneratedDomainScheme}://<project>.{DefaultGeneratedDomainSuffix} by default.";
    private bool _isFirstRunOnboardingDismissed;
    private bool _hasLocalSslMaterial;
    private bool _isLocalSslTrusted;
    private bool _isGlobalNotificationOpen;
    private string _globalNotificationTitle = "Locora";
    private string _globalNotificationMessage = string.Empty;
    private InfoBarSeverity _globalNotificationSeverity = InfoBarSeverity.Informational;
    private string _packageRegistrySummaryText = "Package registry unavailable";
    private string _packageRegistryDetails = "Refresh the supervisor snapshot to load package source status.";
    private int _enabledPackageSourceCount;
    private int _readyPackageSourceCount;
    private int _packageSourceErrorCount;
    private int _catalogPackageCount;
    private int _catalogVersionCount;
    private string _packageDownloadSummaryText = "Package download manager unavailable";
    private string _packageDownloadDetails = "Refresh the supervisor snapshot to load package download planning and cache status.";
    private int _configuredPackageDownloadCount;
    private int _cachedPackageDownloadCount;
    private int _pendingPackageDownloadCount;
    private int _missingPackageDownloadCount;
    private int _packageDownloadErrorCount;
    private int _verifiedPackageChecksumCount;
    private int _unverifiedPackageChecksumCount;
    private int _packageChecksumMismatchCount;
    private int _extractedPackageCount;
    private int _pendingPackageExtractionCount;
    private int _packageExtractionErrorCount;
    private string _runtimePackageSummaryText = "Runtime version inventory unavailable";
    private string _runtimePackageDetails = "Refresh the supervisor snapshot to load PHP, Node.js, Python, Java, and other runtime package versions.";
    private int _runtimePackageCount;
    private int _installedRuntimePackageCount;
    private int _activeRuntimePackageCount;
    private int _switchableRuntimePackageCount;
    private int _runtimePackageAttentionCount;
    private string _toolPackageSummaryText = "Tool package inventory unavailable";
    private string _toolPackageDetails = "Refresh the supervisor snapshot to load Composer, Mailpit, and other tool package inventory.";
    private int _toolPackageCount;
    private int _selectedToolPackageCount;
    private int _installedToolPackageCount;
    private int _activeToolPackageCount;
    private int _toolPackageAttentionCount;
    private string _stackProfileSummaryText = "Stack profiles unavailable";
    private string _stackProfileDetails = "Refresh the supervisor snapshot to load stack profiles.";
    private int _stackProfileCount;
    private int _validStackProfileCount;
    private int _invalidStackProfileCount;
    private string _activeStackProfileKey = "bootstrap";
    private TerminalSession? _selectedTerminalSession;
    private TerminalQuickCommand? _selectedTerminalCommand;
    private string _terminalInputText = string.Empty;
    private string _shellIntegrationRootPath = string.Empty;
    private string _shellContextMenuInstallFilePath = string.Empty;
    private string _shellContextMenuUninstallFilePath = string.Empty;
    private string _shellContextMenuReadmeFilePath = string.Empty;
    private string _shellContextMenuExecutablePath = string.Empty;

    public MainWindowViewModel(
        IWorkbenchService workbenchService,
        IProjectActionLauncher projectActionLauncher,
        ITerminalSessionService terminalSessionService,
        IClipboardService clipboardService,
        IDiagnosticReportService diagnosticReportService,
        IProjectPinStore projectPinStore,
        IOnboardingStateStore onboardingStateStore,
        IShellContextMenuRegistrationService shellContextMenuRegistrationService,
        IEnvironmentPaths environmentPaths,
        IOptions<AppSettings> settings,
        ILogger<MainWindowViewModel> logger)
    {
        _workbenchService = workbenchService;
        _projectActionLauncher = projectActionLauncher;
        _terminalSessionService = terminalSessionService;
        _clipboardService = clipboardService;
        _diagnosticReportService = diagnosticReportService;
        _projectPinStore = projectPinStore;
        _onboardingStateStore = onboardingStateStore;
        _shellContextMenuRegistrationService = shellContextMenuRegistrationService;
        _environmentPaths = environmentPaths;
        _settings = settings.Value;
        _logger = logger;

        EnvironmentRoot = _environmentPaths.AppRoot;
        ConfigRoot = _environmentPaths.ConfigRoot;
        ProjectRoot = _environmentPaths.ProjectRoot;
        BinRoot = _environmentPaths.BinRoot;
        DataRoot = _environmentPaths.DataRoot;
        LogsRoot = _environmentPaths.LogsRoot;
        TempRoot = _environmentPaths.TempRoot;
        HostsPreviewPath = Path.Combine(_environmentPaths.ConfigRoot, "hosts.locora.generated");
        HostsBackupRoot = Path.Combine(_environmentPaths.ConfigRoot, "hosts.backups");
        WindowsHostsFilePath = ResolveWindowsHostsFilePath();
        PipeName = _settings.Supervisor.PipeName;
        PreferredToolchainSummary = $"{_settings.Experience.PreferredWebServer} / {_settings.Experience.PreferredDatabase} / {_settings.Experience.PreferredShell}";
        try
        {
            RefreshShellContextMenuRegistrationFilesCore();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Shell context menu registration files could not be generated during startup.");
            ShellIntegrationRootPath = _environmentPaths.ShellIntegrationRoot;
            ShellContextMenuInstallFilePath = _environmentPaths.ShellContextMenuInstallFile;
            ShellContextMenuUninstallFilePath = _environmentPaths.ShellContextMenuUninstallFile;
        }

        Services = new ObservableCollection<ServiceStatusCard>();
        Projects = new ObservableCollection<ProjectCard>();
        HealthIssues = new ObservableCollection<HealthIssueCard>();
        ValidationResults = new ObservableCollection<ValidationResultCard>();
        PortDiagnostics = new ObservableCollection<PortDiagnosticCard>();
        PermissionDiagnostics = new ObservableCollection<PermissionDiagnosticCard>();
        PackageSources = new ObservableCollection<PackageSourceCard>();
        PackageDownloads = new ObservableCollection<PackageDownloadCard>();
        RuntimePackages = new ObservableCollection<RuntimePackageCard>();
        ToolPackages = new ObservableCollection<ToolPackageCard>();
        StackProfiles = new ObservableCollection<StackProfileCard>();
        SslCertificateDiagnostics = new ObservableCollection<SslCertificateDiagnosticCard>();
        Activity = new ObservableCollection<TimelineEntry>();
        TerminalCommands = new ObservableCollection<TerminalQuickCommand>();
        TerminalSessions = _terminalSessionService.Sessions;
        TerminalSessions.CollectionChanged += OnTerminalSessionsChanged;

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunAction);
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, CanRunAction);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, CanRunAction);
        SaveDomainSettingsCommand = new AsyncRelayCommand(SaveDomainSettingsAsync, CanSaveDomainSettings);
        StartServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StartServiceAsync, CanRunServiceAction);
        StopServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StopServiceAsync, CanRunServiceAction);
        ToggleProjectPinCommand = new AsyncRelayCommand<ProjectCard>(ToggleProjectPinAsync, CanToggleProjectPin);
        RepairRuntimeCommand = new AsyncRelayCommand(RepairRuntimeAsync, CanRunAction);
        RepairDomainsCommand = new AsyncRelayCommand(RepairDomainsAsync, CanRunAction);
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
        DismissFirstRunOnboardingCommand = new AsyncRelayCommand(DismissFirstRunOnboardingAsync, CanRunAction);
        ShowFirstRunOnboardingCommand = new AsyncRelayCommand(ShowFirstRunOnboardingAsync, CanRunAction);
        OpenMailpitInboxCommand = new RelayCommand(OpenMailpitInbox, CanUseMailpitIntegration);
        CopyMailpitSmtpConfigCommand = new RelayCommand(CopyMailpitSmtpConfig, CanUseMailpitIntegration);
        OpenMailpitConnectionDetailsCommand = new RelayCommand(OpenMailpitConnectionDetails, CanOpenMailpitConnectionDetails);
        OpenDatabaseAdminToolCommand = new RelayCommand<ServiceStatusCard>(OpenDatabaseAdminTool, CanUseDatabaseAdminTool);
        CopyDatabaseConnectionCommand = new RelayCommand<ServiceStatusCard>(CopyDatabaseConnection, CanUseDatabaseAdminTool);
        OpenDatabaseConnectionDetailsCommand = new RelayCommand<ServiceStatusCard>(OpenDatabaseConnectionDetails, CanUseDatabaseAdminTool);
        OpenEnvironmentRootCommand = new RelayCommand(OpenEnvironmentRoot, CanUseDesktopPathAction);
        OpenConfigRootCommand = new RelayCommand(OpenConfigRoot, CanUseDesktopPathAction);
        OpenProjectRootCommand = new RelayCommand(OpenProjectRoot, CanUseDesktopPathAction);
        OpenBinRootCommand = new RelayCommand(OpenBinRoot, CanUseDesktopPathAction);
        OpenDataRootCommand = new RelayCommand(OpenDataRoot, CanUseDesktopPathAction);
        OpenLogsRootCommand = new RelayCommand(OpenLogsRoot, CanUseDesktopPathAction);
        OpenTempRootCommand = new RelayCommand(OpenTempRoot, CanUseDesktopPathAction);
        OpenAppSettingsFileCommand = new RelayCommand(OpenAppSettingsFile, CanUseDesktopPathAction);
        OpenSupervisorSettingsFileCommand = new RelayCommand(OpenSupervisorSettingsFile, CanUseDesktopPathAction);
        OpenServicesSettingsFileCommand = new RelayCommand(OpenServicesSettingsFile, CanUseDesktopPathAction);
        OpenProjectsSettingsFileCommand = new RelayCommand(OpenProjectsSettingsFile, CanUseDesktopPathAction);
        OpenProfilesSettingsFileCommand = new RelayCommand(OpenProfilesSettingsFile, CanUseDesktopPathAction);
        OpenPackageSourcesSettingsFileCommand = new RelayCommand(OpenPackageSourcesSettingsFile, CanUseDesktopPathAction);
        OpenPackagesLockSettingsFileCommand = new RelayCommand(OpenPackagesLockSettingsFile, CanUseDesktopPathAction);
        OpenPackageManifestsRootCommand = new RelayCommand(OpenPackageManifestsRoot, CanUseDesktopPathAction);
        OpenPackageCacheRootCommand = new RelayCommand(OpenPackageCacheRoot, CanUseDesktopPathAction);
        SyncPackageDownloadsCommand = new AsyncRelayCommand(SyncPackageDownloadsAsync, CanRunAction);
        ExtractPackageArchivesCommand = new AsyncRelayCommand(ExtractPackageArchivesAsync, CanRunAction);
        InstallOrUpdatePackagesCommand = new AsyncRelayCommand(InstallOrUpdatePackagesAsync, CanRunAction);
        RemovePackageInstallCommand = new AsyncRelayCommand<PackageDownloadCard>(RemovePackageInstallAsync, CanManagePackageInstall);
        SwitchRuntimeVersionCommand = new AsyncRelayCommand<RuntimePackageCard>(SwitchRuntimeVersionAsync, CanRunRuntimeSwitch);
        SelectStackProfileCommand = new AsyncRelayCommand<StackProfileCard>(SelectStackProfileAsync, CanSelectStackProfile);
        OpenProjectUrlCommand = new RelayCommand<ProjectCard>(OpenProjectUrl, CanUseProject);
        OpenProjectFolderCommand = new RelayCommand<ProjectCard>(OpenProjectFolder, CanUseProject);
        RevealProjectInExplorerCommand = new RelayCommand<ProjectCard>(RevealProjectInExplorer, CanUseProject);
        CopyProjectFolderPathCommand = new RelayCommand<ProjectCard>(CopyProjectFolderPath, CanUseProject);
        OpenProjectTerminalCommand = new RelayCommand<ProjectCard>(OpenProjectTerminal, CanUseProject);
        OpenProjectEditorCommand = new RelayCommand<ProjectCard>(OpenProjectEditor, CanUseProject);
        OpenRootTerminalSessionCommand = new RelayCommand(OpenRootTerminalSession);
        SendTerminalInputCommand = new RelayCommand(SendTerminalInput, CanSendTerminalInput);
        StopTerminalSessionCommand = new RelayCommand(StopTerminalSession, CanStopTerminalSession);
        CloseTerminalSessionCommand = new RelayCommand<TerminalSession>(CloseTerminalSession);
        RunTerminalCommandCommand = new RelayCommand<TerminalQuickCommand>(RunTerminalCommand, CanRunTerminalCommand);
        OpenTerminalCommandsSettingsFileCommand = new RelayCommand(OpenTerminalCommandsSettingsFile, CanUseDesktopPathAction);
        OpenAliasesRootCommand = new RelayCommand(OpenAliasesRoot, CanUseDesktopPathAction);
        OpenEnvironmentRootInEditorCommand = new RelayCommand(OpenEnvironmentRootInEditor, CanUseDesktopPathAction);
        OpenProjectRootInEditorCommand = new RelayCommand(OpenProjectRootInEditor, CanUseDesktopPathAction);
        RefreshShellContextMenuFilesCommand = new RelayCommand(RefreshShellContextMenuFiles, CanUseDesktopPathAction);
        OpenShellIntegrationRootCommand = new RelayCommand(OpenShellIntegrationRoot, CanUseDesktopPathAction);
        OpenShellContextMenuInstallFileCommand = new RelayCommand(OpenShellContextMenuInstallFile, CanUseDesktopPathAction);
        OpenShellContextMenuUninstallFileCommand = new RelayCommand(OpenShellContextMenuUninstallFile, CanUseDesktopPathAction);
    }

    public event EventHandler<string>? NavigationRequested;

    public ObservableCollection<ServiceStatusCard> Services { get; }

    public ObservableCollection<ProjectCard> Projects { get; }

    public ObservableCollection<HealthIssueCard> HealthIssues { get; }

    public ObservableCollection<ValidationResultCard> ValidationResults { get; }

    public ObservableCollection<PortDiagnosticCard> PortDiagnostics { get; }

    public ObservableCollection<PermissionDiagnosticCard> PermissionDiagnostics { get; }

    public ObservableCollection<PackageSourceCard> PackageSources { get; }

    public ObservableCollection<PackageDownloadCard> PackageDownloads { get; }

    public ObservableCollection<RuntimePackageCard> RuntimePackages { get; }

    public ObservableCollection<ToolPackageCard> ToolPackages { get; }

    public ObservableCollection<StackProfileCard> StackProfiles { get; }

    public ObservableCollection<SslCertificateDiagnosticCard> SslCertificateDiagnostics { get; }

    public ObservableCollection<TimelineEntry> Activity { get; }

    public ObservableCollection<TerminalQuickCommand> TerminalCommands { get; }

    public ObservableCollection<TerminalSession> TerminalSessions { get; }

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand StartAllCommand { get; }

    public IAsyncRelayCommand StopAllCommand { get; }

    public IAsyncRelayCommand SaveDomainSettingsCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StartServiceCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StopServiceCommand { get; }

    public IAsyncRelayCommand<ProjectCard> ToggleProjectPinCommand { get; }

    public IAsyncRelayCommand RepairRuntimeCommand { get; }

    public IAsyncRelayCommand RepairDomainsCommand { get; }

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

    public IAsyncRelayCommand DismissFirstRunOnboardingCommand { get; }

    public IAsyncRelayCommand ShowFirstRunOnboardingCommand { get; }

    public IRelayCommand OpenMailpitInboxCommand { get; }

    public IRelayCommand CopyMailpitSmtpConfigCommand { get; }

    public IRelayCommand OpenMailpitConnectionDetailsCommand { get; }

    public IRelayCommand<ServiceStatusCard> OpenDatabaseAdminToolCommand { get; }

    public IRelayCommand<ServiceStatusCard> CopyDatabaseConnectionCommand { get; }

    public IRelayCommand<ServiceStatusCard> OpenDatabaseConnectionDetailsCommand { get; }

    public IRelayCommand OpenEnvironmentRootCommand { get; }

    public IRelayCommand OpenConfigRootCommand { get; }

    public IRelayCommand OpenProjectRootCommand { get; }

    public IRelayCommand OpenBinRootCommand { get; }

    public IRelayCommand OpenDataRootCommand { get; }

    public IRelayCommand OpenLogsRootCommand { get; }

    public IRelayCommand OpenTempRootCommand { get; }

    public IRelayCommand OpenAppSettingsFileCommand { get; }

    public IRelayCommand OpenSupervisorSettingsFileCommand { get; }

    public IRelayCommand OpenServicesSettingsFileCommand { get; }

    public IRelayCommand OpenProjectsSettingsFileCommand { get; }

    public IRelayCommand OpenProfilesSettingsFileCommand { get; }

    public IRelayCommand OpenPackageSourcesSettingsFileCommand { get; }

    public IRelayCommand OpenPackagesLockSettingsFileCommand { get; }

    public IRelayCommand OpenPackageManifestsRootCommand { get; }

    public IRelayCommand OpenPackageCacheRootCommand { get; }

    public IAsyncRelayCommand SyncPackageDownloadsCommand { get; }

    public IAsyncRelayCommand ExtractPackageArchivesCommand { get; }

    public IAsyncRelayCommand InstallOrUpdatePackagesCommand { get; }

    public IAsyncRelayCommand<PackageDownloadCard> RemovePackageInstallCommand { get; }

    public IAsyncRelayCommand<RuntimePackageCard> SwitchRuntimeVersionCommand { get; }

    public IAsyncRelayCommand<StackProfileCard> SelectStackProfileCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectUrlCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectFolderCommand { get; }

    public IRelayCommand<ProjectCard> RevealProjectInExplorerCommand { get; }

    public IRelayCommand<ProjectCard> CopyProjectFolderPathCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectTerminalCommand { get; }

    public IRelayCommand<ProjectCard> OpenProjectEditorCommand { get; }

    public IRelayCommand OpenRootTerminalSessionCommand { get; }

    public IRelayCommand SendTerminalInputCommand { get; }

    public IRelayCommand StopTerminalSessionCommand { get; }

    public IRelayCommand<TerminalSession> CloseTerminalSessionCommand { get; }

    public IRelayCommand<TerminalQuickCommand> RunTerminalCommandCommand { get; }

    public IRelayCommand OpenTerminalCommandsSettingsFileCommand { get; }

    public IRelayCommand OpenAliasesRootCommand { get; }

    public IRelayCommand OpenEnvironmentRootInEditorCommand { get; }

    public IRelayCommand OpenProjectRootInEditorCommand { get; }

    public IRelayCommand RefreshShellContextMenuFilesCommand { get; }

    public IRelayCommand OpenShellIntegrationRootCommand { get; }

    public IRelayCommand OpenShellContextMenuInstallFileCommand { get; }

    public IRelayCommand OpenShellContextMenuUninstallFileCommand { get; }

    public string EnvironmentRoot { get; }

    public string ConfigRoot { get; }

    public string ProjectRoot { get; }

    public string BinRoot { get; }

    public string DataRoot { get; }

    public string LogsRoot { get; }

    public string TempRoot { get; }

    public string HostsPreviewPath { get; }

    public string HostsBackupRoot { get; }

    public string WindowsHostsFilePath { get; }

    public string PipeName { get; }

    public string PreferredToolchainSummary { get; }

    public string PreferredEditorLabel => string.IsNullOrWhiteSpace(_settings.Experience.PreferredEditor)
        ? "VS Code"
        : _settings.Experience.PreferredEditor;

    public string EditorIntegrationSummary => "Editor launches prefer a .code-workspace file, then a solution, then a single project file, then the folder. VS Code, VS Code Insiders, Cursor, Windsurf, VSCodium, Visual Studio, and JetBrains IDEs are detected when available. Custom PreferredEditor commands can use {editorTarget}, {workspace}, {solution}, {projectFile}, and {folder}.";

    public string ExplorerContextIntegrationSummary => "Project cards expose a right-click context menu for browser, Explorer, terminal, editor, reveal, copy-path, and pin actions. Windows shell registration files are generated for the current user and can be inspected before applying.";

    public string ShellContextMenuRegistrationSummary => $"Generated HKCU registry files for Windows Explorer using {ShellContextMenuExecutablePath}. Apply the install file to add Locora folder/background menus, or apply the uninstall file to remove them.";

    public string ShellIntegrationRootPath
    {
        get => _shellIntegrationRootPath;
        private set => SetProperty(ref _shellIntegrationRootPath, value);
    }

    public string ShellContextMenuInstallFilePath
    {
        get => _shellContextMenuInstallFilePath;
        private set => SetProperty(ref _shellContextMenuInstallFilePath, value);
    }

    public string ShellContextMenuUninstallFilePath
    {
        get => _shellContextMenuUninstallFilePath;
        private set => SetProperty(ref _shellContextMenuUninstallFilePath, value);
    }

    public string ShellContextMenuReadmeFilePath
    {
        get => _shellContextMenuReadmeFilePath;
        private set => SetProperty(ref _shellContextMenuReadmeFilePath, value);
    }

    public string ShellContextMenuExecutablePath
    {
        get => _shellContextMenuExecutablePath;
        private set
        {
            if (SetProperty(ref _shellContextMenuExecutablePath, value))
            {
                OnPropertyChanged(nameof(ShellContextMenuRegistrationSummary));
            }
        }
    }

    public TerminalSession? SelectedTerminalSession
    {
        get => _selectedTerminalSession;
        set
        {
            if (ReferenceEquals(_selectedTerminalSession, value))
            {
                return;
            }

            if (_selectedTerminalSession is not null)
            {
                _selectedTerminalSession.PropertyChanged -= OnSelectedTerminalSessionPropertyChanged;
            }

            _selectedTerminalSession = value;

            if (_selectedTerminalSession is not null)
            {
                _selectedTerminalSession.PropertyChanged += OnSelectedTerminalSessionPropertyChanged;
            }

            _terminalInputText = _selectedTerminalSession?.PendingInput ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TerminalInputText));
            NotifySelectedTerminalProperties();
            NotifySelectedTerminalCommandProperties();
        }
    }

    public TerminalQuickCommand? SelectedTerminalCommand
    {
        get => _selectedTerminalCommand;
        set
        {
            if (SetProperty(ref _selectedTerminalCommand, value))
            {
                NotifySelectedTerminalCommandProperties();
            }
        }
    }

    public string TerminalInputText
    {
        get => _terminalInputText;
        set
        {
            if (SetProperty(ref _terminalInputText, value))
            {
                if (SelectedTerminalSession is not null)
                {
                    SelectedTerminalSession.PendingInput = value;
                }

                SendTerminalInputCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string TerminalSessionSummary => TerminalSessions.Count == 0
        ? "No built-in terminal tabs are open."
        : $"{TerminalSessions.Count} built-in terminal tab{(TerminalSessions.Count == 1 ? string.Empty : "s")} open.";

    public bool HasTerminalSessions => TerminalSessions.Count > 0;

    public bool ShowTerminalEmptyState => TerminalSessions.Count == 0;

    public bool HasTerminalCommands => TerminalCommands.Count > 0;

    public bool HasSelectedTerminalCommand => SelectedTerminalCommand is not null;

    public bool HasSelectedTerminalSession => SelectedTerminalSession is not null;

    public bool HasSelectedRunningTerminalSession => SelectedTerminalSession?.IsRunning == true;

    public string SelectedTerminalTitle => SelectedTerminalSession?.Title ?? "No session selected";

    public string SelectedTerminalStatus => SelectedTerminalSession?.Status ?? "Start a terminal session or open one from a project card.";

    public string SelectedTerminalOutput => SelectedTerminalSession?.Output ?? string.Empty;

    public string SelectedTerminalCommandHint => SelectedTerminalCommand is null
        ? TerminalCommands.Count == 0
            ? "No preset commands are configured yet. Open the commands file to add some."
            : "Pick a preset command to run it in the active terminal tab."
        : NormalizeTerminalCommandScope(SelectedTerminalCommand.Scope) == "project" &&
            TryGetProjectTerminalSessionForCommand() is null
                ? $"{SelectedTerminalCommand.ScopeLabel}: {SelectedTerminalCommand.Summary} Open a project terminal tab first."
                : $"{SelectedTerminalCommand.ScopeLabel}: {SelectedTerminalCommand.Summary}";

    public string SelectedTerminalCommandResolvedText => SelectedTerminalCommand is null
        ? string.Empty
        : ResolveTerminalCommandText(SelectedTerminalCommand);

    public string ProjectsSettingsFilePath => _environmentPaths.ProjectsSettingsFile;

    public string ProfilesSettingsFilePath => _environmentPaths.ProfilesSettingsFile;

    public string OnboardingSettingsFilePath => _environmentPaths.OnboardingSettingsFile;

    public string AppSettingsFilePath => _environmentPaths.AppSettingsFile;

    public string SupervisorSettingsFilePath => _environmentPaths.SupervisorSettingsFile;

    public string ServicesSettingsFilePath => _environmentPaths.ServicesSettingsFile;

    public string PackageSourcesSettingsFilePath => _environmentPaths.PackageSourcesSettingsFile;

    public string PackagesLockSettingsFilePath => _environmentPaths.PackagesLockSettingsFile;

    public string TerminalCommandsSettingsFilePath => _environmentPaths.TerminalCommandsSettingsFile;

    public string AliasesRootPath => _environmentPaths.AliasesRoot;

    public string ProjectPinsSettingsFilePath => _environmentPaths.ProjectPinsSettingsFile;

    public IReadOnlyList<string> DomainSchemes => GeneratedDomainSchemes;

    public string PackageManifestsRootPath => _environmentPaths.PackageManifestsRoot;

    public string PackageCacheRootPath => _environmentPaths.PackageCacheRoot;

    public string GeneratedDomainSuffix
    {
        get => _generatedDomainSuffix;
        set
        {
            if (SetProperty(ref _generatedDomainSuffix, value))
            {
                OnPropertyChanged(nameof(DomainSettingsPreview));
                SaveDomainSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string GeneratedDomainScheme
    {
        get => _generatedDomainScheme;
        set
        {
            if (SetProperty(ref _generatedDomainScheme, NormalizeGeneratedDomainScheme(value)))
            {
                OnPropertyChanged(nameof(DomainSettingsPreview));
                SaveDomainSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DomainSettingsPreview => TryNormalizeGeneratedDomainSettings(out var normalizedSuffix, out var normalizedScheme, out var validationMessage)
        ? $"Preview: {normalizedScheme}://example.{normalizedSuffix}"
        : validationMessage;

    public string DomainSettingsStatus
    {
        get => _domainSettingsStatus;
        private set => SetProperty(ref _domainSettingsStatus, value);
    }

    public bool HasServices => Services.Count > 0;

    public bool ShowServicesEmptyState => !HasServices;

    public bool HasProjects => Projects.Count > 0;

    public bool ShowProjectsEmptyState => !HasProjects;

    public bool HasHealthIssues => HealthIssues.Count > 0;

    public bool ShowHealthIssuesEmptyState => !HasHealthIssues;

    public bool HasValidationResults => ValidationResults.Count > 0;

    public bool ShowValidationResultsEmptyState => !HasValidationResults;

    public bool HasPortDiagnostics => PortDiagnostics.Count > 0;

    public bool ShowPortDiagnosticsEmptyState => !HasPortDiagnostics;

    public bool HasPermissionDiagnostics => PermissionDiagnostics.Count > 0;

    public bool ShowPermissionDiagnosticsEmptyState => !HasPermissionDiagnostics;

    public bool HasPackageSources => PackageSources.Count > 0;

    public bool ShowPackageSourcesEmptyState => !HasPackageSources;

    public bool HasPackageDownloads => PackageDownloads.Count > 0;

    public bool ShowPackageDownloadsEmptyState => !HasPackageDownloads;

    public bool HasRuntimePackages => RuntimePackages.Count > 0;

    public bool ShowRuntimePackagesEmptyState => !HasRuntimePackages;

    public bool HasToolPackages => ToolPackages.Count > 0;

    public bool ShowToolPackagesEmptyState => !HasToolPackages;

    public bool HasStackProfiles => StackProfiles.Count > 0;

    public bool ShowStackProfilesEmptyState => !HasStackProfiles;

    public bool HasSslCertificateDiagnostics => SslCertificateDiagnostics.Count > 0;

    public bool ShowSslCertificateDiagnosticsEmptyState => !HasSslCertificateDiagnostics;

    public bool HasActivity => Activity.Count > 0;

    public bool ShowActivityEmptyState => !HasActivity;

    public bool IsFirstRunOnboardingDismissed
    {
        get => _isFirstRunOnboardingDismissed;
        private set
        {
            if (SetProperty(ref _isFirstRunOnboardingDismissed, value))
            {
                OnPropertyChanged(nameof(ShowFirstRunOnboarding));
                OnPropertyChanged(nameof(FirstRunOnboardingStatusLabel));
            }
        }
    }

    public bool ShowFirstRunOnboarding => !IsFirstRunOnboardingDismissed;

    public string FirstRunOnboardingStatusLabel => ShowFirstRunOnboarding
        ? "First-run guide is visible on the dashboard."
        : "First-run guide is hidden. You can show it again any time.";

    public int FirstRunCompletedStepCount
    {
        get
        {
            var completed = 0;
            completed += IsSupervisorConnected ? 1 : 0;
            completed += HasServices ? 1 : 0;
            completed += HasProjects ? 1 : 0;
            completed += _hasLocalSslMaterial ? 1 : 0;
            completed += HasValidationResults ? 1 : 0;

            return completed;
        }
    }

    public string FirstRunProgressLabel => $"{FirstRunCompletedStepCount}/5 setup checks ready";

    public string FirstRunSupervisorStepLabel => IsSupervisorConnected
        ? $"Ready: {SupervisorProcessLabel}"
        : "Waiting: refresh or start Locora.Supervisor to load live state.";

    public string FirstRunServicesStepLabel => HasServices
        ? $"Ready: {TotalServicesCount} services configured, {RunningServicesCount} running."
        : "Waiting: repair runtime configs to seed services from bundled defaults.";

    public string FirstRunProjectsStepLabel => HasProjects
        ? $"Ready: {Projects.Count} projects discovered under {ProjectRoot}."
        : $"Waiting: add a project folder under {ProjectRoot}, then refresh.";

    public string FirstRunSslStepLabel => _hasLocalSslMaterial
        ? _isLocalSslTrusted
            ? "Ready: local SSL material exists and the CurrentUser CA is trusted."
            : "Almost ready: local SSL material exists; trust the CA if you want browser-trusted HTTPS."
        : "Waiting: generate local SSL when you want HTTPS project URLs.";

    public string FirstRunValidationStepLabel => HasValidationResults
        ? ConfigValidationSummary
        : "Waiting: run Nginx validation before relying on generated vhosts.";

    public bool IsGlobalNotificationOpen
    {
        get => _isGlobalNotificationOpen;
        set => SetProperty(ref _isGlobalNotificationOpen, value);
    }

    public string GlobalNotificationTitle
    {
        get => _globalNotificationTitle;
        private set => SetProperty(ref _globalNotificationTitle, value);
    }

    public string GlobalNotificationMessage
    {
        get => _globalNotificationMessage;
        private set => SetProperty(ref _globalNotificationMessage, value);
    }

    public InfoBarSeverity GlobalNotificationSeverity
    {
        get => _globalNotificationSeverity;
        private set => SetProperty(ref _globalNotificationSeverity, value);
    }

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
                SaveDomainSettingsCommand.NotifyCanExecuteChanged();
                StartServiceCommand.NotifyCanExecuteChanged();
                StopServiceCommand.NotifyCanExecuteChanged();
                ToggleProjectPinCommand.NotifyCanExecuteChanged();
                RepairRuntimeCommand.NotifyCanExecuteChanged();
                RepairDomainsCommand.NotifyCanExecuteChanged();
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
                DismissFirstRunOnboardingCommand.NotifyCanExecuteChanged();
                ShowFirstRunOnboardingCommand.NotifyCanExecuteChanged();
                OpenMailpitInboxCommand.NotifyCanExecuteChanged();
                CopyMailpitSmtpConfigCommand.NotifyCanExecuteChanged();
                OpenMailpitConnectionDetailsCommand.NotifyCanExecuteChanged();
                OpenDatabaseAdminToolCommand.NotifyCanExecuteChanged();
                CopyDatabaseConnectionCommand.NotifyCanExecuteChanged();
                OpenDatabaseConnectionDetailsCommand.NotifyCanExecuteChanged();
                OpenEnvironmentRootCommand.NotifyCanExecuteChanged();
                OpenConfigRootCommand.NotifyCanExecuteChanged();
                OpenProjectRootCommand.NotifyCanExecuteChanged();
                OpenBinRootCommand.NotifyCanExecuteChanged();
                OpenDataRootCommand.NotifyCanExecuteChanged();
                OpenLogsRootCommand.NotifyCanExecuteChanged();
                OpenTempRootCommand.NotifyCanExecuteChanged();
                OpenAppSettingsFileCommand.NotifyCanExecuteChanged();
                OpenSupervisorSettingsFileCommand.NotifyCanExecuteChanged();
                OpenServicesSettingsFileCommand.NotifyCanExecuteChanged();
                OpenProjectsSettingsFileCommand.NotifyCanExecuteChanged();
                OpenProfilesSettingsFileCommand.NotifyCanExecuteChanged();
                OpenPackageSourcesSettingsFileCommand.NotifyCanExecuteChanged();
                OpenPackagesLockSettingsFileCommand.NotifyCanExecuteChanged();
                OpenTerminalCommandsSettingsFileCommand.NotifyCanExecuteChanged();
                OpenAliasesRootCommand.NotifyCanExecuteChanged();
                OpenEnvironmentRootInEditorCommand.NotifyCanExecuteChanged();
                OpenProjectRootInEditorCommand.NotifyCanExecuteChanged();
                RefreshShellContextMenuFilesCommand.NotifyCanExecuteChanged();
                OpenShellIntegrationRootCommand.NotifyCanExecuteChanged();
                OpenShellContextMenuInstallFileCommand.NotifyCanExecuteChanged();
                OpenShellContextMenuUninstallFileCommand.NotifyCanExecuteChanged();
                OpenPackageManifestsRootCommand.NotifyCanExecuteChanged();
                OpenPackageCacheRootCommand.NotifyCanExecuteChanged();
                SyncPackageDownloadsCommand.NotifyCanExecuteChanged();
                ExtractPackageArchivesCommand.NotifyCanExecuteChanged();
                InstallOrUpdatePackagesCommand.NotifyCanExecuteChanged();
                RemovePackageInstallCommand.NotifyCanExecuteChanged();
                SwitchRuntimeVersionCommand.NotifyCanExecuteChanged();
                SelectStackProfileCommand.NotifyCanExecuteChanged();
                OpenProjectUrlCommand.NotifyCanExecuteChanged();
                OpenProjectFolderCommand.NotifyCanExecuteChanged();
                RevealProjectInExplorerCommand.NotifyCanExecuteChanged();
                CopyProjectFolderPathCommand.NotifyCanExecuteChanged();
                OpenProjectTerminalCommand.NotifyCanExecuteChanged();
                OpenProjectEditorCommand.NotifyCanExecuteChanged();
                RunTerminalCommandCommand.NotifyCanExecuteChanged();
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

    public string PackageRegistrySummaryText
    {
        get => _packageRegistrySummaryText;
        private set => SetProperty(ref _packageRegistrySummaryText, value);
    }

    public string PackageRegistryDetails
    {
        get => _packageRegistryDetails;
        private set => SetProperty(ref _packageRegistryDetails, value);
    }

    public string PackageRegistryStatusLabel
    {
        get
        {
            if (_enabledPackageSourceCount == 0)
            {
                return "No package sources enabled";
            }

            var readyLabel = $"{_readyPackageSourceCount}/{_enabledPackageSourceCount} enabled sources ready";
            return _packageSourceErrorCount == 0
                ? readyLabel
                : $"{readyLabel}, {_packageSourceErrorCount} with errors";
        }
    }

    public string PackageRegistryCatalogLabel => $"{_catalogPackageCount} packages / {_catalogVersionCount} versions cataloged";

    public string PackageDownloadSummaryText
    {
        get => _packageDownloadSummaryText;
        private set => SetProperty(ref _packageDownloadSummaryText, value);
    }

    public string PackageDownloadDetails
    {
        get => _packageDownloadDetails;
        private set => SetProperty(ref _packageDownloadDetails, value);
    }

    public string PackageDownloadStatusLabel
    {
        get
        {
            if (_configuredPackageDownloadCount == 0)
            {
                return "No package downloads configured";
            }

            return $"{_cachedPackageDownloadCount}/{_configuredPackageDownloadCount} cached, {_pendingPackageDownloadCount} pending, {_missingPackageDownloadCount} missing, {_packageDownloadErrorCount} errors";
        }
    }

    public string PackageDownloadChecksumLabel
    {
        get
        {
            if (_configuredPackageDownloadCount == 0)
            {
                return "Checksums unavailable until package selections are configured";
            }

            return $"{_verifiedPackageChecksumCount} verified, {_unverifiedPackageChecksumCount} unverified, {_packageChecksumMismatchCount} mismatched";
        }
    }

    public string PackageExtractionStatusLabel
    {
        get
        {
            if (_configuredPackageDownloadCount == 0)
            {
                return "Extraction unavailable until package selections are configured";
            }

            return $"{_extractedPackageCount}/{_configuredPackageDownloadCount} extracted, {_pendingPackageExtractionCount} pending, {_packageExtractionErrorCount} errors";
        }
    }

    public string RuntimePackageSummaryText
    {
        get => _runtimePackageSummaryText;
        private set => SetProperty(ref _runtimePackageSummaryText, value);
    }

    public string RuntimePackageDetails
    {
        get => _runtimePackageDetails;
        private set => SetProperty(ref _runtimePackageDetails, value);
    }

    public string RuntimePackageStatusLabel
    {
        get
        {
            if (_runtimePackageCount == 0)
            {
                return "No runtime packages cataloged";
            }

            return $"{_activeRuntimePackageCount}/{_runtimePackageCount} active, {_installedRuntimePackageCount} installed, {_switchableRuntimePackageCount} switchable, {_runtimePackageAttentionCount} need attention";
        }
    }

    public string ToolPackageSummaryText
    {
        get => _toolPackageSummaryText;
        private set => SetProperty(ref _toolPackageSummaryText, value);
    }

    public string ToolPackageDetails
    {
        get => _toolPackageDetails;
        private set => SetProperty(ref _toolPackageDetails, value);
    }

    public string ToolPackageStatusLabel
    {
        get
        {
            if (_toolPackageCount == 0)
            {
                return "No tool packages cataloged";
            }

            return $"{_selectedToolPackageCount}/{_toolPackageCount} selected, {_installedToolPackageCount} installed, {_activeToolPackageCount} active, {_toolPackageAttentionCount} need attention";
        }
    }

    public string StackProfileSummaryText
    {
        get => _stackProfileSummaryText;
        private set => SetProperty(ref _stackProfileSummaryText, value);
    }

    public string StackProfileDetails
    {
        get => _stackProfileDetails;
        private set => SetProperty(ref _stackProfileDetails, value);
    }

    public string StackProfileStatusLabel
    {
        get
        {
            if (_stackProfileCount == 0)
            {
                return "No stack profiles configured";
            }

            return $"{_validStackProfileCount}/{_stackProfileCount} valid, {_invalidStackProfileCount} need attention, active {_activeStackProfileKey}";
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
        await LoadOnboardingStateAsync();
        await LoadProjectPinsAsync();
        await LoadDomainSettingsAsync();
        await LoadTerminalCommandsAsync();
        await RefreshAsync();
    }

    private bool CanRunAction() => !IsBusy;

    private bool CanSaveDomainSettings()
    {
        return !IsBusy && TryNormalizeGeneratedDomainSettings(out _, out _, out _);
    }

    private bool CanToggleProjectPin(ProjectCard? project) => !IsBusy && project is not null;

    private bool CanRunServiceAction(ServiceStatusCard? service) => !IsBusy && service is not null;

    private bool CanManagePackageInstall(PackageDownloadCard? package) => !IsBusy && package is not null && package.CanRemoveInstall;

    private bool CanRunRuntimeSwitch(RuntimePackageCard? package) => !IsBusy && package is not null;

    private bool CanSelectStackProfile(StackProfileCard? profile) => !IsBusy && profile?.CanSelect == true;

    private bool CanUseDiagnosticReport() => !IsBusy && _lastSnapshot is not null;

    private bool CanUseMailpitIntegration() => !IsBusy && ShowMailpitIntegration;

    private bool CanOpenMailpitConnectionDetails() => !IsBusy && _mailpitConnectionDetailsExists;

    private bool CanUseDatabaseAdminTool(ServiceStatusCard? service) => !IsBusy && service?.SupportsDatabaseAdminTool == true;

    private bool CanUseDesktopPathAction() => !IsBusy;

    private bool CanRepairServiceAction(ServiceStatusCard? service)
    {
        if (IsBusy || service is null)
        {
            return false;
        }

        return service.SupportsRepair;
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

    private async Task ToggleProjectPinAsync(ProjectCard? project)
    {
        if (project is null)
        {
            return;
        }

        var nextPinned = !_pinnedProjectKeys.Contains(project.Key);
        if (nextPinned)
        {
            _pinnedProjectKeys.Add(project.Key);
        }
        else
        {
            _pinnedProjectKeys.Remove(project.Key);
        }

        try
        {
            await _projectPinStore.SavePinnedProjectKeysAsync(_pinnedProjectKeys);

            if (_lastSnapshot is not null)
            {
                ApplyProjects(_lastSnapshot.Projects);
            }

            RecordActivity("Info", nextPinned ? $"Pinned project: {project.Name}" : $"Unpinned project: {project.Name}");
        }
        catch (Exception exception)
        {
            if (nextPinned)
            {
                _pinnedProjectKeys.Remove(project.Key);
            }
            else
            {
                _pinnedProjectKeys.Add(project.Key);
            }

            _logger.LogWarning(exception, "Failed to update pinned project state for {Project}", project.Name);
            RecordActivity("Error", $"Pinned projects update failed for {project.Name}: {exception.Message}");
        }
    }

    private async Task RepairRuntimeAsync()
    {
        await ExecuteAsync(
            "Repair runtime configs requested",
            () => _workbenchService.RepairRuntimeAsync());
    }

    private async Task SaveDomainSettingsAsync()
    {
        if (!TryNormalizeGeneratedDomainSettings(out var normalizedSuffix, out var normalizedScheme, out var validationMessage))
        {
            DomainSettingsStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        try
        {
            IsBusy = true;
            RecordActivity("Info", "Saving generated domain defaults.");

            var currentDocument = NormalizeProjectSettingsDocument(await ReadProjectSettingsDocumentAsync());
            var nextDocument = new ProjectSettingsDocument(
                currentDocument.LocoraProjects with
                {
                    DomainSuffix = normalizedSuffix,
                    DefaultScheme = normalizedScheme,
                    IndexFileNames = currentDocument.LocoraProjects.IndexFileNames,
                    IgnoredDirectoryNames = currentDocument.LocoraProjects.IgnoredDirectoryNames
                });

            Directory.CreateDirectory(Path.GetDirectoryName(_environmentPaths.ProjectsSettingsFile) ?? _environmentPaths.ConfigRoot);
            var content = JsonSerializer.Serialize(nextDocument, ProjectSettingsSerializerOptions);
            await File.WriteAllTextAsync(_environmentPaths.ProjectsSettingsFile, content);

            GeneratedDomainSuffix = normalizedSuffix;
            GeneratedDomainScheme = normalizedScheme;
            DomainSettingsStatus = $"Saved. Generated URLs now default to {normalizedScheme}://<project>.{normalizedSuffix}.";
            RecordActivity("Info", $"Project domain defaults saved: {normalizedScheme}://<project>.{normalizedSuffix}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to save project domain settings.");
            DomainSettingsStatus = $"Could not save project domain settings: {exception.Message}";
            RecordActivity("Error", DomainSettingsStatus);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ExecuteAsync(
            "Regenerating hosts and vhosts for updated domain defaults",
            () => _workbenchService.RepairDomainsAsync());
    }

    private async Task RepairDomainsAsync()
    {
        await ExecuteAsync(
            "Repair hosts and vhosts requested",
            () => _workbenchService.RepairDomainsAsync());
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

    private async Task SyncPackageDownloadsAsync()
    {
        await ExecuteAsync(
            "Syncing package downloads into cache",
            () => _workbenchService.SyncPackageDownloadsAsync());
    }

    private async Task ExtractPackageArchivesAsync()
    {
        await ExecuteAsync(
            "Extracting cached package archives into bin",
            () => _workbenchService.ExtractPackageArchivesAsync());
    }

    private async Task InstallOrUpdatePackagesAsync()
    {
        await ExecuteAsync(
            "Installing or updating active packages",
            () => _workbenchService.InstallOrUpdatePackagesAsync());
    }

    private async Task RemovePackageInstallAsync(PackageDownloadCard? package)
    {
        if (package is null)
        {
            return;
        }

        await ExecuteAsync(
            $"Removing installed versions for {package.Name}",
            () => _workbenchService.RemovePackageInstallAsync(package.Id));
    }

    private async Task SwitchRuntimeVersionAsync(RuntimePackageCard? package)
    {
        if (package is null || !package.CanSwitchVersion)
        {
            return;
        }

        await ExecuteAsync(
            $"Selecting {package.Name} {package.SelectedVersion}",
            () => _workbenchService.SelectRuntimeVersionAsync(package.Id, package.SelectedVersion));
    }

    private async Task SelectStackProfileAsync(StackProfileCard? profile)
    {
        if (profile is null || !profile.CanSelect)
        {
            return;
        }

        await ExecuteAsync(
            $"Selecting stack profile {profile.DisplayName}",
            () => _workbenchService.SelectStackProfileAsync(profile.Key));
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

    private async Task DismissFirstRunOnboardingAsync()
    {
        await SaveFirstRunOnboardingVisibilityAsync(isDismissed: true, "First-run onboarding hidden.");
    }

    private async Task ShowFirstRunOnboardingAsync()
    {
        await SaveFirstRunOnboardingVisibilityAsync(isDismissed: false, "First-run onboarding shown.");
    }

    private async Task SaveFirstRunOnboardingVisibilityAsync(bool isDismissed, string activityMessage)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _onboardingStateStore.SaveFirstRunDismissedAsync(isDismissed);
            IsFirstRunOnboardingDismissed = isDismissed;
            RecordActivity("Info", activityMessage);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist first-run onboarding state.");
            RecordActivity("Error", $"First-run onboarding state could not be saved: {exception.Message}");
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

    private void OpenDatabaseAdminTool(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        try
        {
            _projectActionLauncher.OpenDatabaseAdminTool(service.Key, EnvironmentRoot);
            RecordActivity("Info", $"Opened database admin tool: {service.Name}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Database admin tool launch failed for {Service}.", service.Name);
            RecordActivity("Error", $"{service.Name}: database admin tool could not be opened. {exception.Message}");
        }
    }

    private void CopyDatabaseConnection(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        try
        {
            _clipboardService.CopyText(BuildDatabaseConnectionSnippet(service));
            RecordActivity("Info", $"Database connection copied: {service.Name}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Database connection copy failed for {Service}.", service.Name);
            RecordActivity("Error", $"{service.Name}: database connection could not be copied. {exception.Message}");
        }
    }

    private void OpenDatabaseConnectionDetails(ServiceStatusCard? service)
    {
        if (service is null)
        {
            return;
        }

        var details = LoadDatabaseConnectionDetails(service);
        try
        {
            if (details.ConnectionDetailsExists)
            {
                _projectActionLauncher.OpenFile(details.ConnectionDetailsPath);
                RecordActivity("Info", $"Database connection details opened: {service.Name}");
                return;
            }

            _projectActionLauncher.OpenFolder(details.ConfigRoot);
            RecordActivity("Info", $"Database config folder opened: {service.Name}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Database connection details launch failed for {Service}.", service.Name);
            RecordActivity("Error", $"{service.Name}: database connection details could not be opened. {exception.Message}");
        }
    }

    private void OpenEnvironmentRoot()
    {
        ExecuteDesktopAction(
            "Portable root opened.",
            "Portable root could not be opened.",
            () => _projectActionLauncher.OpenFolder(EnvironmentRoot));
    }

    private void OpenConfigRoot()
    {
        ExecuteDesktopAction(
            "Config folder opened.",
            "Config folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(ConfigRoot));
    }

    private void OpenProjectRoot()
    {
        ExecuteDesktopAction(
            "Projects root opened.",
            "Projects root could not be opened.",
            () => _projectActionLauncher.OpenFolder(ProjectRoot));
    }

    private void OpenBinRoot()
    {
        ExecuteDesktopAction(
            "Runtime binaries folder opened.",
            "Runtime binaries folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(BinRoot));
    }

    private void OpenDataRoot()
    {
        ExecuteDesktopAction(
            "Data folder opened.",
            "Data folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(DataRoot));
    }

    private void OpenLogsRoot()
    {
        ExecuteDesktopAction(
            "Logs folder opened.",
            "Logs folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(LogsRoot));
    }

    private void OpenTempRoot()
    {
        ExecuteDesktopAction(
            "Temp folder opened.",
            "Temp folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(TempRoot));
    }

    private void OpenAppSettingsFile()
    {
        ExecuteDesktopAction(
            "App settings file opened.",
            "App settings file could not be opened.",
            () => _projectActionLauncher.OpenFile(AppSettingsFilePath));
    }

    private void OpenSupervisorSettingsFile()
    {
        ExecuteDesktopAction(
            "Supervisor settings file opened.",
            "Supervisor settings file could not be opened.",
            () => _projectActionLauncher.OpenFile(SupervisorSettingsFilePath));
    }

    private void OpenServicesSettingsFile()
    {
        ExecuteDesktopAction(
            "Services settings file opened.",
            "Services settings file could not be opened.",
            () => _projectActionLauncher.OpenFile(ServicesSettingsFilePath));
    }

    private void OpenProjectsSettingsFile()
    {
        ExecuteDesktopAction(
            "Project discovery settings file opened.",
            "Project discovery settings file could not be opened.",
            () => _projectActionLauncher.OpenFile(ProjectsSettingsFilePath));
    }

    private void OpenProfilesSettingsFile()
    {
        ExecuteDesktopAction(
            "Stack profiles file opened.",
            "Stack profiles file could not be opened.",
            () => _projectActionLauncher.OpenFile(ProfilesSettingsFilePath));
    }

    private void OpenPackageSourcesSettingsFile()
    {
        ExecuteDesktopAction(
            "Package sources file opened.",
            "Package sources file could not be opened.",
            () => _projectActionLauncher.OpenFile(PackageSourcesSettingsFilePath));
    }

    private void OpenPackagesLockSettingsFile()
    {
        ExecuteDesktopAction(
            "Packages lock file opened.",
            "Packages lock file could not be opened.",
            () => _projectActionLauncher.OpenFile(PackagesLockSettingsFilePath));
    }

    private void OpenTerminalCommandsSettingsFile()
    {
        ExecuteDesktopAction(
            "Terminal commands file opened.",
            "Terminal commands file could not be opened.",
            () => _projectActionLauncher.OpenFile(TerminalCommandsSettingsFilePath));
    }

    private void OpenAliasesRoot()
    {
        ExecuteDesktopAction(
            "Aliases folder opened.",
            "Aliases folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(AliasesRootPath));
    }

    private void RefreshShellContextMenuFiles()
    {
        ExecuteDesktopAction(
            "Shell context menu registration files refreshed.",
            "Shell context menu registration files could not be refreshed.",
            RefreshShellContextMenuRegistrationFilesCore);
    }

    private void OpenShellIntegrationRoot()
    {
        ExecuteDesktopAction(
            "Shell integration folder opened.",
            "Shell integration folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(ShellIntegrationRootPath));
    }

    private void OpenShellContextMenuInstallFile()
    {
        ExecuteDesktopAction(
            "Shell context menu install file opened.",
            "Shell context menu install file could not be opened.",
            () => _projectActionLauncher.OpenFile(ShellContextMenuInstallFilePath));
    }

    private void OpenShellContextMenuUninstallFile()
    {
        ExecuteDesktopAction(
            "Shell context menu uninstall file opened.",
            "Shell context menu uninstall file could not be opened.",
            () => _projectActionLauncher.OpenFile(ShellContextMenuUninstallFilePath));
    }

    private void RefreshShellContextMenuRegistrationFilesCore()
    {
        var files = _shellContextMenuRegistrationService.RefreshRegistrationFiles();
        ShellIntegrationRootPath = files.RootPath;
        ShellContextMenuInstallFilePath = files.InstallFilePath;
        ShellContextMenuUninstallFilePath = files.UninstallFilePath;
        ShellContextMenuReadmeFilePath = files.ReadmeFilePath;
        ShellContextMenuExecutablePath = files.AppExecutablePath;
    }

    private void OpenEnvironmentRootInEditor()
    {
        ExecuteDesktopAction(
            "Portable root opened in the preferred editor.",
            "Portable root could not be opened in the preferred editor.",
            () => _projectActionLauncher.OpenEditor(EnvironmentRoot));
    }

    private void OpenProjectRootInEditor()
    {
        ExecuteDesktopAction(
            "Projects root opened in the preferred editor.",
            "Projects root could not be opened in the preferred editor.",
            () => _projectActionLauncher.OpenEditor(ProjectRoot));
    }

    private void OpenPackageManifestsRoot()
    {
        ExecuteDesktopAction(
            "Package manifests folder opened.",
            "Package manifests folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(PackageManifestsRootPath));
    }

    private void OpenPackageCacheRoot()
    {
        ExecuteDesktopAction(
            "Package cache folder opened.",
            "Package cache folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(PackageCacheRootPath));
    }

    private void OpenProjectUrl(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project URL", card => _projectActionLauncher.OpenUrl(card.Url));
    }

    private void OpenProjectFolder(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project folder", card => _projectActionLauncher.OpenFolder(card.Folder));
    }

    private void RevealProjectInExplorer(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Revealed project in Explorer", card => _projectActionLauncher.RevealInExplorer(card.Folder));
    }

    private void CopyProjectFolderPath(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Copied project folder path", card => _clipboardService.CopyText(card.Folder));
    }

    private void OpenProjectTerminal(ProjectCard? project)
    {
        if (project is null)
        {
            return;
        }

        StartTerminalSession(project.Name, project.TerminalLaunchProfile, $"Opened built-in terminal: {project.Name}");
    }

    private void OpenProjectEditor(ProjectCard? project)
    {
        ExecuteProjectAction(project, "Info", "Opened project editor", card => _projectActionLauncher.OpenEditor(card.Folder));
    }

    public void HandleShellContextMenuCommand(ShellContextMenuCommand command)
    {
        if (!TryResolveShellContextTarget(command.TargetPath, out var targetPath, out var folderPath, out var errorMessage))
        {
            RecordActivity("Error", errorMessage);
            return;
        }

        try
        {
            switch (command.Action.ToLowerInvariant())
            {
                case "open":
                    _projectActionLauncher.OpenFolder(folderPath);
                    RecordActivity("Info", $"Explorer context opened folder: {folderPath}");
                    break;
                case "reveal":
                    _projectActionLauncher.RevealInExplorer(targetPath);
                    RecordActivity("Info", $"Explorer context revealed: {targetPath}");
                    break;
                case "editor":
                    _projectActionLauncher.OpenEditor(folderPath);
                    RecordActivity("Info", $"Explorer context opened editor: {folderPath}");
                    break;
                case "terminal":
                    StartTerminalSession(
                        CreateShellContextTerminalTitle(folderPath),
                        CreateShellContextTerminalLaunchProfile(folderPath),
                        $"Opened built-in terminal from Explorer: {folderPath}");
                    break;
                default:
                    RecordActivity("Warning", $"Unsupported Explorer context action: {command.Action}");
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Explorer context command failed for {Action} {TargetPath}.", command.Action, command.TargetPath);
            RecordActivity("Error", $"Explorer context action failed: {exception.Message}");
        }
    }

    private void OpenRootTerminalSession()
    {
        StartTerminalSession("Locora root", CreateRootTerminalLaunchProfile(), "Opened built-in terminal: Locora root");
    }

    private bool CanRunTerminalCommand(TerminalQuickCommand? command)
    {
        if (IsBusy || command is null || string.IsNullOrWhiteSpace(command.CommandText))
        {
            return false;
        }

        return NormalizeTerminalCommandScope(command.Scope) != "project" ||
            TryGetProjectTerminalSessionForCommand() is not null;
    }

    private void RunTerminalCommand(TerminalQuickCommand? command)
    {
        if (command is null)
        {
            return;
        }

        try
        {
            var session = ResolveTerminalSessionForCommand(command);
            if (session is null)
            {
                RecordActivity("Warning", "Select a project terminal tab before running project-scoped commands.");
                return;
            }

            var commandText = ResolveTerminalCommandText(command, session);
            if (string.IsNullOrWhiteSpace(commandText))
            {
                RecordActivity("Warning", $"Terminal command '{command.DisplayName}' resolved to an empty string.");
                return;
            }

            SelectedTerminalSession = session;
            NavigationRequested?.Invoke(this, "terminal");
            _terminalSessionService.SendInput(session, commandText);
            RecordActivity("Info", $"Ran terminal command: {command.DisplayName}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Terminal preset command failed for {Command}.", command.DisplayName);
            RecordActivity("Error", $"Terminal command failed for {command.DisplayName}: {exception.Message}");
        }
    }

    private bool CanSendTerminalInput()
    {
        return SelectedTerminalSession?.IsRunning == true &&
            !string.IsNullOrWhiteSpace(TerminalInputText);
    }

    private void SendTerminalInput()
    {
        if (SelectedTerminalSession is null || string.IsNullOrWhiteSpace(TerminalInputText))
        {
            return;
        }

        _terminalSessionService.SendInput(SelectedTerminalSession, TerminalInputText);
        TerminalInputText = string.Empty;
    }

    private bool CanStopTerminalSession()
    {
        return SelectedTerminalSession?.IsRunning == true;
    }

    private void StopTerminalSession()
    {
        if (SelectedTerminalSession is null)
        {
            return;
        }

        _terminalSessionService.StopSession(SelectedTerminalSession);
        RecordActivity("Info", $"Stopped built-in terminal: {SelectedTerminalSession.Title}");
    }

    private void CloseTerminalSession(TerminalSession? session)
    {
        if (session is null)
        {
            return;
        }

        _terminalSessionService.CloseSession(session);
        RecordActivity("Info", $"Closed terminal tab: {session.Title}");
    }

    private TerminalSession? ResolveTerminalSessionForCommand(TerminalQuickCommand command)
    {
        var scope = NormalizeTerminalCommandScope(command.Scope);
        if (scope == "root")
        {
            return ResolveOrStartRootTerminalSession();
        }

        if (scope == "project")
        {
            var projectSession = TryGetProjectTerminalSessionForCommand();
            if (projectSession is null)
            {
                return null;
            }

            return EnsureRunningTerminalSession(projectSession);
        }

        if (SelectedTerminalSession is not null)
        {
            return EnsureRunningTerminalSession(SelectedTerminalSession);
        }

        return ResolveOrStartRootTerminalSession();
    }

    private TerminalSession? ResolveOrStartRootTerminalSession()
    {
        if (SelectedTerminalSession is { IsProjectScoped: false } selectedRootSession)
        {
            return EnsureRunningTerminalSession(selectedRootSession);
        }

        var existingRootSession = TerminalSessions.LastOrDefault(session => !session.IsProjectScoped);
        return existingRootSession is not null
            ? EnsureRunningTerminalSession(existingRootSession)
            : StartTerminalSession("Locora root", CreateRootTerminalLaunchProfile(), "Opened built-in terminal: Locora root");
    }

    private TerminalSession? TryGetProjectTerminalSessionForCommand()
    {
        if (SelectedTerminalSession?.IsProjectScoped == true)
        {
            return SelectedTerminalSession;
        }

        return TerminalSessions.LastOrDefault(session => session.IsProjectScoped);
    }

    private TerminalSession? EnsureRunningTerminalSession(TerminalSession session)
    {
        if (session.IsRunning)
        {
            return session;
        }

        return StartTerminalSession(session.Title, session.LaunchProfile, $"Reopened built-in terminal: {session.Title}");
    }

    private TerminalSession? StartTerminalSession(string title, ProjectTerminalLaunchProfile launchProfile, string successMessage)
    {
        try
        {
            var session = _terminalSessionService.StartSession(title, launchProfile);
            SelectedTerminalSession = session;
            TerminalInputText = string.Empty;
            NavigationRequested?.Invoke(this, "terminal");
            RecordActivity("Info", successMessage);
            return session;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Built-in terminal failed for {Title}.", title);
            RecordActivity("Error", $"{title}: built-in terminal failed: {exception.Message}");
            return null;
        }
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

        ApplyProjects(snapshot.Projects);

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
            PackageSources,
            snapshot.PackageSources.Select(source => new PackageSourceCard(
                source.Id,
                source.DisplayName,
                source.IsEnabled ? source.State : "Disabled",
                CreatePackageSourceLabel(source),
                $"{source.PackageCount} packages / {source.VersionCount} versions",
                source.ManifestPath,
                source.Summary,
                source.Details,
                source.IsEnabled)));

        ReplaceCollection(
            PackageDownloads,
            snapshot.PackageDownloads.Select(download => new PackageDownloadCard(
                download.PackageId,
                download.DisplayName,
                download.State,
                CreatePackageDownloadChecksumLabel(download),
                CreatePackageDownloadExtractionLabel(download),
                $"Requested {download.RequestedVersion} / resolved {download.ResolvedVersion}",
                $"Source {download.SourceId}",
                download.ArtifactSource,
                download.CachePath,
                download.InstallPath,
                download.ActivePath,
                download.Summary,
                download.Details,
                download.IsCached,
                download.IsExtracted,
                CanRemovePackageInstall(download),
                RemovePackageInstallCommand)));

        ReplaceCollection(
            RuntimePackages,
            snapshot.RuntimePackages.Select(runtime => new RuntimePackageCard(
                runtime.PackageId,
                runtime.DisplayName,
                runtime.Family,
                CreateRuntimePackageStateLabel(runtime),
                CreateRuntimePackageVersionLabel(runtime),
                CreateRuntimePackageInstalledLabel(runtime),
                $"Source {runtime.SourceId} / {runtime.Channel}",
                CreateRuntimePackagePathLabel(runtime),
                runtime.Summary,
                runtime.Details,
                runtime.AvailableVersions,
                runtime.InstalledVersions,
                SelectRuntimePackageVersion(runtime),
                runtime.ActiveVersion,
                runtime.SupportsSwitching,
                runtime.IsInstalled,
                runtime.IsActive,
                SwitchRuntimeVersionCommand)));

        ReplaceCollection(
            ToolPackages,
            snapshot.ToolPackages.Select(tool => new ToolPackageCard(
                tool.PackageId,
                tool.DisplayName,
                tool.Family,
                CreateToolPackageStateLabel(tool),
                CreateToolPackageVersionLabel(tool),
                CreateToolPackageCommandsLabel(tool),
                $"Source {tool.SourceId} / {tool.Channel}",
                CreateToolPackagePathLabel(tool),
                tool.Summary,
                tool.Details,
                tool.IsSelected,
                tool.IsInstalled,
                tool.IsActive)));

        ReplaceCollection(
            StackProfiles,
            snapshot.StackProfiles.Select(profile => new StackProfileCard(
                profile.Key,
                profile.DisplayName,
                profile.Description,
                profile.State,
                CreateStackProfileServicesLabel(profile),
                CreateStackProfilePackagesLabel(profile),
                CreateTagsLabel(profile.Tags),
                profile.IsActive,
                profile.IsValid,
                profile.Summary,
                profile.Details,
                SelectStackProfileCommand)));

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
        _hasLocalSslMaterial = snapshot.SslStatus.CaExists ||
            snapshot.SslStatus.ProjectCertificateCount > 0;
        _isLocalSslTrusted = snapshot.SslStatus.TrustSupported && snapshot.SslStatus.IsCurrentUserTrusted;
        PackageRegistrySummaryText = snapshot.PackageRegistry.Summary;
        PackageRegistryDetails = snapshot.PackageRegistry.Details;
        _enabledPackageSourceCount = snapshot.PackageRegistry.EnabledSourceCount;
        _readyPackageSourceCount = snapshot.PackageRegistry.ReadySourceCount;
        _packageSourceErrorCount = snapshot.PackageRegistry.ErrorSourceCount;
        _catalogPackageCount = snapshot.PackageRegistry.PackageCount;
        _catalogVersionCount = snapshot.PackageRegistry.VersionCount;
        PackageDownloadSummaryText = snapshot.PackageDownloadsSummary.Summary;
        PackageDownloadDetails = snapshot.PackageDownloadsSummary.Details;
        _configuredPackageDownloadCount = snapshot.PackageDownloadsSummary.ActiveSelectionCount;
        _cachedPackageDownloadCount = snapshot.PackageDownloadsSummary.CachedCount;
        _pendingPackageDownloadCount = snapshot.PackageDownloadsSummary.PendingCount;
        _missingPackageDownloadCount = snapshot.PackageDownloadsSummary.MissingCount;
        _packageDownloadErrorCount = snapshot.PackageDownloadsSummary.ErrorCount;
        _verifiedPackageChecksumCount = snapshot.PackageDownloadsSummary.VerifiedChecksumCount;
        _unverifiedPackageChecksumCount = snapshot.PackageDownloadsSummary.UnverifiedChecksumCount;
        _packageChecksumMismatchCount = snapshot.PackageDownloadsSummary.ChecksumMismatchCount;
        _extractedPackageCount = snapshot.PackageDownloadsSummary.ExtractedCount;
        _pendingPackageExtractionCount = snapshot.PackageDownloadsSummary.PendingExtractionCount;
        _packageExtractionErrorCount = snapshot.PackageDownloadsSummary.ExtractionErrorCount;
        RuntimePackageSummaryText = snapshot.RuntimePackageSummary.Summary;
        RuntimePackageDetails = snapshot.RuntimePackageSummary.Details;
        _runtimePackageCount = snapshot.RuntimePackageSummary.RuntimeCount;
        _installedRuntimePackageCount = snapshot.RuntimePackageSummary.InstalledCount;
        _activeRuntimePackageCount = snapshot.RuntimePackageSummary.ActiveCount;
        _switchableRuntimePackageCount = snapshot.RuntimePackageSummary.SwitchableCount;
        _runtimePackageAttentionCount = snapshot.RuntimePackageSummary.AttentionCount;
        ToolPackageSummaryText = snapshot.ToolPackageSummary.Summary;
        ToolPackageDetails = snapshot.ToolPackageSummary.Details;
        _toolPackageCount = snapshot.ToolPackageSummary.ToolCount;
        _selectedToolPackageCount = snapshot.ToolPackageSummary.SelectedCount;
        _installedToolPackageCount = snapshot.ToolPackageSummary.InstalledCount;
        _activeToolPackageCount = snapshot.ToolPackageSummary.ActiveCount;
        _toolPackageAttentionCount = snapshot.ToolPackageSummary.AttentionCount;
        StackProfileSummaryText = snapshot.StackProfileSummary.Summary;
        StackProfileDetails = snapshot.StackProfileSummary.Details;
        _stackProfileCount = snapshot.StackProfileSummary.ProfileCount;
        _validStackProfileCount = snapshot.StackProfileSummary.ValidProfileCount;
        _invalidStackProfileCount = snapshot.StackProfileSummary.InvalidProfileCount;
        _activeStackProfileKey = snapshot.StackProfileSummary.ActiveProfileKey;
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
        OnPropertyChanged(nameof(PackageRegistryStatusLabel));
        OnPropertyChanged(nameof(PackageRegistryCatalogLabel));
        OnPropertyChanged(nameof(PackageDownloadStatusLabel));
        OnPropertyChanged(nameof(PackageDownloadChecksumLabel));
        OnPropertyChanged(nameof(PackageExtractionStatusLabel));
        OnPropertyChanged(nameof(RuntimePackageStatusLabel));
        OnPropertyChanged(nameof(ToolPackageStatusLabel));
        OnPropertyChanged(nameof(StackProfileStatusLabel));
        OnPropertyChanged(nameof(HostsAccessDiagnosticLabel));
        OnPropertyChanged(nameof(ElevationRestartDiagnosticLabel));
        OnPropertyChanged(nameof(WorkspaceWriteAccessDiagnosticLabel));
        OnPropertyChanged(nameof(SslTrustAccessDiagnosticLabel));
        NotifyTerminalCommandCollectionProperties();
        NotifySelectedTerminalCommandProperties();
        NotifyCollectionStateProperties();
        NotifyFirstRunOnboardingProperties();
        CopyDiagnosticReportCommand.NotifyCanExecuteChanged();
        ExportDiagnosticReportCommand.NotifyCanExecuteChanged();
        OpenMailpitInboxCommand.NotifyCanExecuteChanged();
        CopyMailpitSmtpConfigCommand.NotifyCanExecuteChanged();
        OpenMailpitConnectionDetailsCommand.NotifyCanExecuteChanged();

        RecordActivity(
            snapshot.IsSupervisorReachable ? "Info" : "Warning",
            $"Snapshot applied with {RunningServicesCount}/{TotalServicesCount} services active.");
    }

    private void ApplyProjects(IReadOnlyList<ProjectDescriptor> projects)
    {
        ReplaceCollection(Projects, CreateProjectCards(projects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ShowProjectsEmptyState));
        NotifyFirstRunOnboardingProperties();
    }

    private IEnumerable<ProjectCard> CreateProjectCards(IReadOnlyList<ProjectDescriptor> projects)
    {
        return projects
            .Select(project =>
            {
                var projectKey = CreateProjectKey(project.Path);
                var isPinned = _pinnedProjectKeys.Contains(projectKey);

                return new ProjectCard(
                    projectKey,
                    project.Name,
                    project.Url,
                    project.Runtime,
                    project.Path,
                    CreateProjectTerminalLaunchProfile(project),
                    project.Description,
                    CreateTagsLabel(project.Tags),
                    isPinned,
                    isPinned ? "Unpin" : "Pin",
                    ToggleProjectPinCommand,
                    OpenProjectUrlCommand,
                    OpenProjectFolderCommand,
                    RevealProjectInExplorerCommand,
                    CopyProjectFolderPathCommand,
                    OpenProjectTerminalCommand,
                    OpenProjectEditorCommand);
            })
            .OrderByDescending(project => project.IsPinned)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Folder, StringComparer.OrdinalIgnoreCase);
    }

    private ProjectTerminalLaunchProfile CreateProjectTerminalLaunchProfile(ProjectDescriptor project)
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["APP_URL"] = project.Url,
            ["LOCORA_ALIASES_ROOT"] = _environmentPaths.AliasesRoot,
            ["LOCORA_ROOT"] = _environmentPaths.AppRoot,
            ["LOCORA_BIN"] = _environmentPaths.BinRoot,
            ["LOCORA_CONFIG"] = _environmentPaths.ConfigRoot,
            ["LOCORA_DATA"] = _environmentPaths.DataRoot,
            ["LOCORA_LOGS"] = _environmentPaths.LogsRoot,
            ["LOCORA_TEMP"] = _environmentPaths.TempRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_PROFILE"] = _lastSnapshot?.ActiveProfile ?? "Bootstrap",
            ["LOCORA_PROJECT_NAME"] = project.Name,
            ["LOCORA_PROJECT_ROOT"] = project.Path,
            ["LOCORA_PROJECT_URL"] = project.Url,
            ["LOCORA_PROJECT_RUNTIME"] = project.Runtime,
            ["LOCORA_PROJECT_HTTPS"] = project.UsesHttps ? "1" : "0",
            ["LOCORA_PROJECT_TAGS"] = string.Join(",", project.Tags)
        };

        if (Uri.TryCreate(project.Url, UriKind.Absolute, out var projectUri))
        {
            environmentVariables["LOCORA_PROJECT_HOST"] = projectUri.Host;
            environmentVariables["LOCORA_PROJECT_SCHEME"] = projectUri.Scheme;
        }

        var pathEntries = new List<string>();
        pathEntries.Add(_environmentPaths.AliasesRoot);
        ApplyTerminalPackageEnvironment(environmentVariables, pathEntries, project.Runtime);

        return new ProjectTerminalLaunchProfile(
            project.Path,
            environmentVariables,
            pathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private ProjectTerminalLaunchProfile CreateRootTerminalLaunchProfile()
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCORA_ALIASES_ROOT"] = _environmentPaths.AliasesRoot,
            ["LOCORA_ROOT"] = _environmentPaths.AppRoot,
            ["LOCORA_BIN"] = _environmentPaths.BinRoot,
            ["LOCORA_CONFIG"] = _environmentPaths.ConfigRoot,
            ["LOCORA_DATA"] = _environmentPaths.DataRoot,
            ["LOCORA_LOGS"] = _environmentPaths.LogsRoot,
            ["LOCORA_TEMP"] = _environmentPaths.TempRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_PROFILE"] = _lastSnapshot?.ActiveProfile ?? "Bootstrap"
        };
        var pathEntries = new List<string>();
        pathEntries.Add(_environmentPaths.AliasesRoot);
        ApplyTerminalPackageEnvironment(environmentVariables, pathEntries, runtimeLabel: string.Empty);

        return new ProjectTerminalLaunchProfile(
            _environmentPaths.AppRoot,
            environmentVariables,
            pathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private ProjectTerminalLaunchProfile CreateShellContextTerminalLaunchProfile(string folderPath)
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCORA_ALIASES_ROOT"] = _environmentPaths.AliasesRoot,
            ["LOCORA_ROOT"] = _environmentPaths.AppRoot,
            ["LOCORA_BIN"] = _environmentPaths.BinRoot,
            ["LOCORA_CONFIG"] = _environmentPaths.ConfigRoot,
            ["LOCORA_DATA"] = _environmentPaths.DataRoot,
            ["LOCORA_LOGS"] = _environmentPaths.LogsRoot,
            ["LOCORA_TEMP"] = _environmentPaths.TempRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_PROFILE"] = _lastSnapshot?.ActiveProfile ?? "Bootstrap",
            ["LOCORA_SHELL_CONTEXT_ROOT"] = folderPath,
            ["LOCORA_WORKING_DIRECTORY"] = folderPath
        };
        var pathEntries = new List<string>();
        pathEntries.Add(_environmentPaths.AliasesRoot);
        ApplyTerminalPackageEnvironment(environmentVariables, pathEntries, runtimeLabel: string.Empty);

        return new ProjectTerminalLaunchProfile(
            folderPath,
            environmentVariables,
            pathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void ApplyTerminalPackageEnvironment(
        IDictionary<string, string> environmentVariables,
        ICollection<string> pathEntries,
        string runtimeLabel)
    {
        foreach (var runtime in SelectProjectTerminalRuntimes(runtimeLabel))
        {
            var runtimeRoot = ResolveTerminalRuntimeRoot(runtime);
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                continue;
            }

            pathEntries.Add(runtimeRoot);

            var environmentToken = NormalizeRuntimeEnvironmentToken(runtime);
            environmentVariables[$"LOCORA_{environmentToken}_ROOT"] = runtimeRoot;

            if (!string.IsNullOrWhiteSpace(runtime.ActiveVersion) &&
                !runtime.ActiveVersion.Equals("Unavailable", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables[$"LOCORA_{environmentToken}_VERSION"] = runtime.ActiveVersion;
            }

            var runtimeBinaryPath = ResolveTerminalRuntimeBinaryPath(runtime, runtimeRoot);
            if (string.IsNullOrWhiteSpace(runtimeBinaryPath))
            {
                continue;
            }

            environmentVariables[$"LOCORA_{environmentToken}_BINARY"] = runtimeBinaryPath;
            AddTerminalExecutableDirectory(pathEntries, runtimeBinaryPath);

            if (runtime.Family.Equals("php", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables["PHP_BINARY"] = runtimeBinaryPath;
            }
            else if (runtime.Family.Equals("python", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables["PYTHONHOME"] = runtimeRoot;
                environmentVariables["PYTHONEXECUTABLE"] = runtimeBinaryPath;

                var scriptsPath = Path.Combine(runtimeRoot, "Scripts");
                if (Directory.Exists(scriptsPath))
                {
                    pathEntries.Add(scriptsPath);
                }
            }
            else if (runtime.Family.Equals("java", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables["JAVA_HOME"] = runtimeRoot;
            }
        }

        foreach (var tool in SelectProjectTerminalTools())
        {
            var toolRoot = ResolveTerminalToolRoot(tool);
            if (string.IsNullOrWhiteSpace(toolRoot))
            {
                continue;
            }

            pathEntries.Add(toolRoot);

            var environmentToken = NormalizeToolEnvironmentToken(tool);
            environmentVariables[$"LOCORA_TOOL_{environmentToken}_ROOT"] = toolRoot;

            if (!string.IsNullOrWhiteSpace(tool.ActiveVersion) &&
                !tool.ActiveVersion.Equals("(none)", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables[$"LOCORA_TOOL_{environmentToken}_VERSION"] = tool.ActiveVersion;
            }

            var toolBinaryPath = ResolveTerminalToolBinaryPath(tool, toolRoot);
            if (string.IsNullOrWhiteSpace(toolBinaryPath))
            {
                continue;
            }

            environmentVariables[$"LOCORA_TOOL_{environmentToken}_BINARY"] = toolBinaryPath;
            AddTerminalExecutableDirectory(pathEntries, toolBinaryPath);
        }
    }

    private IEnumerable<RuntimePackageStatus> SelectProjectTerminalRuntimes(string runtimeLabel)
    {
        return (_lastSnapshot?.RuntimePackages ?? Array.Empty<RuntimePackageStatus>())
            .Where(runtime => runtime.Kind.Equals("runtime", StringComparison.OrdinalIgnoreCase))
            .Where(IsTerminalRuntimeReady)
            .OrderBy(runtime => ScoreRuntimeForProject(runtime, runtimeLabel))
            .ThenBy(runtime => runtime.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<ToolPackageStatus> SelectProjectTerminalTools()
    {
        return (_lastSnapshot?.ToolPackages ?? Array.Empty<ToolPackageStatus>())
            .Where(tool => tool.IsSelected || tool.IsInstalled || tool.IsActive)
            .Where(IsTerminalToolReady)
            .OrderByDescending(tool => tool.IsActive)
            .ThenByDescending(tool => tool.IsInstalled)
            .ThenBy(tool => tool.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static int ScoreRuntimeForProject(RuntimePackageStatus runtime, string runtimeLabel)
    {
        if (string.IsNullOrWhiteSpace(runtimeLabel))
        {
            return 2;
        }

        if (runtimeLabel.Contains("PHP", StringComparison.OrdinalIgnoreCase) &&
            runtime.Family.Equals("php", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (runtimeLabel.Contains("Node", StringComparison.OrdinalIgnoreCase) &&
            runtime.Family.Equals("nodejs", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (runtimeLabel.Contains("Python", StringComparison.OrdinalIgnoreCase) &&
            runtime.Family.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (runtimeLabel.Contains("Java", StringComparison.OrdinalIgnoreCase) &&
            runtime.Family.Equals("java", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (runtime.Family.Equals("nodejs", StringComparison.OrdinalIgnoreCase) &&
            (runtimeLabel.Contains("PHP", StringComparison.OrdinalIgnoreCase) ||
             runtimeLabel.Contains("Static", StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        return 2;
    }

    private static string ResolveTerminalRuntimeRoot(RuntimePackageStatus runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ActivePath) &&
            Directory.Exists(runtime.ActivePath))
        {
            return runtime.ActivePath;
        }

        var executableDirectory = Path.GetDirectoryName(runtime.ExecutablePath);
        return !string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory)
            ? executableDirectory
            : string.Empty;
    }

    private static string ResolveTerminalRuntimeBinaryPath(RuntimePackageStatus runtime, string runtimeRoot)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ActivePath) &&
            Directory.Exists(runtime.ActivePath) &&
            TryResolveTerminalBinaryPath(runtime.InstallRootPath, runtime.ResolvedVersion, runtime.ExecutablePath, runtime.ActivePath, out var activeBinaryPath) &&
            File.Exists(activeBinaryPath))
        {
            return activeBinaryPath;
        }

        if (!string.IsNullOrWhiteSpace(runtime.ExecutablePath) &&
            File.Exists(runtime.ExecutablePath))
        {
            return runtime.ExecutablePath;
        }

        if (TryResolveTerminalBinaryPath(runtime.InstallRootPath, runtime.ResolvedVersion, runtime.ExecutablePath, runtimeRoot, out var runtimeBinaryPath))
        {
            return runtimeBinaryPath;
        }

        var executableName = Path.GetFileName(runtime.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return string.Empty;
        }

        return Path.Combine(runtimeRoot, executableName);
    }

    private static bool IsTerminalRuntimeReady(RuntimePackageStatus runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ActivePath) &&
            Directory.Exists(runtime.ActivePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(runtime.ExecutablePath) &&
            File.Exists(runtime.ExecutablePath))
        {
            return true;
        }

        var executableDirectory = Path.GetDirectoryName(runtime.ExecutablePath);
        return !string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory);
    }

    private static string ResolveTerminalToolRoot(ToolPackageStatus tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.ActivePath) &&
            Directory.Exists(tool.ActivePath))
        {
            return tool.ActivePath;
        }

        var executableDirectory = Path.GetDirectoryName(tool.ExecutablePath);
        return !string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory)
            ? executableDirectory
            : string.Empty;
    }

    private static string ResolveTerminalToolBinaryPath(ToolPackageStatus tool, string toolRoot)
    {
        if (!string.IsNullOrWhiteSpace(tool.ActivePath) &&
            Directory.Exists(tool.ActivePath) &&
            TryResolveTerminalBinaryPath(tool.InstallRootPath, tool.ResolvedVersion, tool.ExecutablePath, tool.ActivePath, out var activeBinaryPath) &&
            File.Exists(activeBinaryPath))
        {
            return activeBinaryPath;
        }

        if (!string.IsNullOrWhiteSpace(tool.ExecutablePath) &&
            File.Exists(tool.ExecutablePath))
        {
            return tool.ExecutablePath;
        }

        if (TryResolveTerminalBinaryPath(tool.InstallRootPath, tool.ResolvedVersion, tool.ExecutablePath, toolRoot, out var toolBinaryPath))
        {
            return toolBinaryPath;
        }

        var executableName = Path.GetFileName(tool.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return string.Empty;
        }

        return Path.Combine(toolRoot, executableName);
    }

    private static bool IsTerminalToolReady(ToolPackageStatus tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.ActivePath) &&
            Directory.Exists(tool.ActivePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(tool.ExecutablePath) &&
            File.Exists(tool.ExecutablePath))
        {
            return true;
        }

        var executableDirectory = Path.GetDirectoryName(tool.ExecutablePath);
        return !string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory);
    }

    private static void AddTerminalExecutableDirectory(ICollection<string> pathEntries, string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(executableDirectory) &&
            Directory.Exists(executableDirectory))
        {
            pathEntries.Add(executableDirectory);
        }
    }

    private static bool TryResolveTerminalBinaryPath(
        string installRootPath,
        string version,
        string executablePath,
        string targetRoot,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(installRootPath) ||
            string.IsNullOrWhiteSpace(version) ||
            string.IsNullOrWhiteSpace(executablePath) ||
            string.IsNullOrWhiteSpace(targetRoot) ||
            !Path.IsPathRooted(installRootPath) ||
            !Path.IsPathRooted(executablePath) ||
            !Path.IsPathRooted(targetRoot) ||
            version.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
            version.Equals("(none)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var versionRoot = Path.Combine(installRootPath, version);
        string relativeExecutablePath;

        try
        {
            relativeExecutablePath = Path.GetRelativePath(versionRoot, executablePath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativeExecutablePath) ||
            relativeExecutablePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        resolvedPath = Path.Combine(targetRoot, relativeExecutablePath);
        return true;
    }

    private static string NormalizeToolEnvironmentToken(ToolPackageStatus tool)
    {
        var candidate = string.IsNullOrWhiteSpace(tool.Family) ? tool.PackageId : tool.Family;
        return new string(candidate
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizeRuntimeEnvironmentToken(RuntimePackageStatus runtime)
    {
        var candidate = string.IsNullOrWhiteSpace(runtime.Family) ? runtime.PackageId : runtime.Family;
        return new string(candidate
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string CreateTagsLabel(IReadOnlyList<string> tags)
    {
        return tags.Count == 0 ? "Tags: none" : $"Tags: {string.Join(", ", tags)}";
    }

    private static string CreateStackProfileServicesLabel(StackProfileStatus profile)
    {
        return profile.ServiceKeys.Count == 0
            ? "Services: all configured services"
            : $"Services: {string.Join(", ", profile.ServiceKeys)}";
    }

    private static string CreateStackProfilePackagesLabel(StackProfileStatus profile)
    {
        return profile.PackageSelections.Count == 0
            ? "Packages: none declared"
            : $"Packages: {string.Join(", ", profile.PackageSelections.Select(selection => $"{selection.Key} {selection.Value}"))}";
    }

    private static string CreatePackageSourceLabel(PackageSourceStatus source)
    {
        var channel = string.IsNullOrWhiteSpace(source.Channel) ? "channel unavailable" : source.Channel;
        var kind = string.IsNullOrWhiteSpace(source.Kind) ? "source" : source.Kind;
        return $"{kind} source / {channel} / priority {source.Priority}";
    }

    private static string CreatePackageDownloadChecksumLabel(PackageDownloadStatus download)
    {
        return download.ChecksumState switch
        {
            "Verified" => "Checksum: verified",
            "Unverified" => string.IsNullOrWhiteSpace(download.ActualSha256)
                ? "Checksum: unverified"
                : $"Checksum: unverified / computed {AbbreviateHash(download.ActualSha256)}",
            "Mismatch" => $"Checksum: mismatch / expected {AbbreviateHash(download.ExpectedSha256)}",
            "Pending" => string.IsNullOrWhiteSpace(download.ExpectedSha256)
                ? "Checksum: waiting for artifact"
                : $"Checksum: waiting for artifact / expected {AbbreviateHash(download.ExpectedSha256)}",
            _ => string.IsNullOrWhiteSpace(download.ExpectedSha256)
                ? $"Checksum: {download.ChecksumState.ToLowerInvariant()}"
                : $"Checksum: {download.ChecksumState.ToLowerInvariant()} / expected {AbbreviateHash(download.ExpectedSha256)}"
        };
    }

    private static string CreatePackageDownloadExtractionLabel(PackageDownloadStatus download)
    {
        return download.ExtractionState switch
        {
            "Extracted" => string.IsNullOrWhiteSpace(download.ActivePath) || download.ActivePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase)
                ? $"Extraction: ready / {download.InstallPath}"
                : $"Extraction: ready / current path {download.ActivePath}",
            "Pending" => string.IsNullOrWhiteSpace(download.InstallPath) || download.InstallPath.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase)
                ? "Extraction: pending"
                : $"Extraction: pending / install to {download.InstallPath}",
            "Unavailable" => "Extraction: waiting for cached artifact",
            _ => string.IsNullOrWhiteSpace(download.InstallPath) || download.InstallPath.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase)
                ? $"Extraction: {download.ExtractionState.ToLowerInvariant()}"
                : $"Extraction: {download.ExtractionState.ToLowerInvariant()} / {download.InstallPath}"
        };
    }

    private static bool CanRemovePackageInstall(PackageDownloadStatus download)
    {
        if (download.IsExtracted)
        {
            return true;
        }

        var hasInstallRoot = !string.IsNullOrWhiteSpace(download.InstallPath) &&
            !download.InstallPath.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(download.InstallPath);
        var hasActivePath = !string.IsNullOrWhiteSpace(download.ActivePath) &&
            !download.ActivePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(download.ActivePath);

        return hasInstallRoot || hasActivePath;
    }

    private static string CreateRuntimePackageStateLabel(RuntimePackageStatus runtime)
    {
        return runtime.State switch
        {
            "Active" => "Active runtime",
            "Installed" => "Installed, not active",
            "Cataloged" => "Available in catalog",
            "MissingArtifact" => "Artifact missing",
            "PendingDownload" => "Download pending",
            "ReadyToExtract" => "Ready to extract",
            "Error" => "Needs attention",
            _ => runtime.State
        };
    }

    private static string CreateRuntimePackageVersionLabel(RuntimePackageStatus runtime)
    {
        return $"Requested {runtime.RequestedVersion} / resolved {runtime.ResolvedVersion} / active {runtime.ActiveVersion}";
    }

    private static string CreateRuntimePackageInstalledLabel(RuntimePackageStatus runtime)
    {
        return runtime.InstalledVersions.Count == 0
            ? "Installed versions: none"
            : $"Installed versions: {string.Join(", ", runtime.InstalledVersions)}";
    }

    private static string CreateRuntimePackagePathLabel(RuntimePackageStatus runtime)
    {
        return $"Install root {runtime.InstallRootPath} / active path {runtime.ActivePath}";
    }

    private static string CreateToolPackageStateLabel(ToolPackageStatus tool)
    {
        return tool.State switch
        {
            "Active" => "Active tool",
            "Installed" => "Installed tool",
            "Cataloged" => "Cataloged tool",
            "ReadyToExtract" => "Cached and ready to extract",
            "PendingDownload" => "Waiting for artifact sync",
            "MissingArtifact" => "Artifact missing",
            "Error" => "Tool package error",
            _ => tool.State
        };
    }

    private static string CreateToolPackageVersionLabel(ToolPackageStatus tool)
    {
        return $"Requested {tool.RequestedVersion} / resolved {tool.ResolvedVersion} / active {tool.ActiveVersion}";
    }

    private static string CreateToolPackageCommandsLabel(ToolPackageStatus tool)
    {
        return tool.ProvidedCommands.Count == 0
            ? "Commands: none declared"
            : $"Commands: {string.Join(", ", tool.ProvidedCommands)}";
    }

    private static string CreateToolPackagePathLabel(ToolPackageStatus tool)
    {
        return $"Install root {tool.InstallRootPath} / active path {tool.ActivePath}";
    }

    private static string SelectRuntimePackageVersion(RuntimePackageStatus runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.ResolvedVersion) &&
            !runtime.ResolvedVersion.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) &&
            runtime.AvailableVersions.Any(version => version.Equals(runtime.ResolvedVersion, StringComparison.OrdinalIgnoreCase)))
        {
            return runtime.ResolvedVersion;
        }

        return runtime.AvailableVersions.FirstOrDefault() ?? string.Empty;
    }

    private static string AbbreviateHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(unset)";
        }

        var normalized = value.Trim();
        return normalized.Length <= 16
            ? normalized
            : $"{normalized[..12]}...";
    }

    private void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void OnTerminalSessionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (SelectedTerminalSession is null && TerminalSessions.Count > 0)
        {
            SelectedTerminalSession = TerminalSessions[^1];
        }
        else if (SelectedTerminalSession is not null && !TerminalSessions.Contains(SelectedTerminalSession))
        {
            SelectedTerminalSession = TerminalSessions.LastOrDefault();
        }

        NotifyTerminalCollectionProperties();
        NotifySelectedTerminalCommandProperties();
    }

    private void OnSelectedTerminalSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TerminalSession.Output))
        {
            OnPropertyChanged(nameof(SelectedTerminalOutput));
        }

        if (args.PropertyName is nameof(TerminalSession.Status) or nameof(TerminalSession.IsRunning))
        {
            NotifySelectedTerminalProperties();
        }

        if (args.PropertyName is nameof(TerminalSession.WorkingDirectory) or nameof(TerminalSession.IsRunning))
        {
            NotifySelectedTerminalCommandProperties();
        }
    }

    private void NotifyTerminalCollectionProperties()
    {
        OnPropertyChanged(nameof(TerminalSessionSummary));
        OnPropertyChanged(nameof(HasTerminalSessions));
        OnPropertyChanged(nameof(ShowTerminalEmptyState));
    }

    private void NotifyTerminalCommandCollectionProperties()
    {
        OnPropertyChanged(nameof(HasTerminalCommands));
        OnPropertyChanged(nameof(HasSelectedTerminalCommand));
        OnPropertyChanged(nameof(SelectedTerminalCommandHint));
        OnPropertyChanged(nameof(SelectedTerminalCommandResolvedText));
    }

    private void NotifySelectedTerminalCommandProperties()
    {
        OnPropertyChanged(nameof(HasSelectedTerminalCommand));
        OnPropertyChanged(nameof(SelectedTerminalCommandHint));
        OnPropertyChanged(nameof(SelectedTerminalCommandResolvedText));
        RunTerminalCommandCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedTerminalProperties()
    {
        OnPropertyChanged(nameof(HasSelectedTerminalSession));
        OnPropertyChanged(nameof(HasSelectedRunningTerminalSession));
        OnPropertyChanged(nameof(SelectedTerminalTitle));
        OnPropertyChanged(nameof(SelectedTerminalStatus));
        OnPropertyChanged(nameof(SelectedTerminalOutput));
        SendTerminalInputCommand.NotifyCanExecuteChanged();
        StopTerminalSessionCommand.NotifyCanExecuteChanged();
        RunTerminalCommandCommand.NotifyCanExecuteChanged();
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

        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(ShowActivityEmptyState));
        NotifyFirstRunOnboardingProperties();

        if (ShouldShowGlobalNotification(level, message))
        {
            ShowGlobalNotification(level, message);
        }
    }

    private void NotifyCollectionStateProperties()
    {
        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(ShowServicesEmptyState));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(ShowProjectsEmptyState));
        OnPropertyChanged(nameof(HasHealthIssues));
        OnPropertyChanged(nameof(ShowHealthIssuesEmptyState));
        OnPropertyChanged(nameof(HasValidationResults));
        OnPropertyChanged(nameof(ShowValidationResultsEmptyState));
        OnPropertyChanged(nameof(HasPortDiagnostics));
        OnPropertyChanged(nameof(ShowPortDiagnosticsEmptyState));
        OnPropertyChanged(nameof(HasPermissionDiagnostics));
        OnPropertyChanged(nameof(ShowPermissionDiagnosticsEmptyState));
        OnPropertyChanged(nameof(HasPackageSources));
        OnPropertyChanged(nameof(ShowPackageSourcesEmptyState));
        OnPropertyChanged(nameof(HasPackageDownloads));
        OnPropertyChanged(nameof(ShowPackageDownloadsEmptyState));
        OnPropertyChanged(nameof(HasRuntimePackages));
        OnPropertyChanged(nameof(ShowRuntimePackagesEmptyState));
        OnPropertyChanged(nameof(HasToolPackages));
        OnPropertyChanged(nameof(ShowToolPackagesEmptyState));
        OnPropertyChanged(nameof(HasStackProfiles));
        OnPropertyChanged(nameof(ShowStackProfilesEmptyState));
        OnPropertyChanged(nameof(HasSslCertificateDiagnostics));
        OnPropertyChanged(nameof(ShowSslCertificateDiagnosticsEmptyState));
        NotifyTerminalCollectionProperties();
        NotifySelectedTerminalProperties();
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(ShowActivityEmptyState));
    }

    private void NotifyFirstRunOnboardingProperties()
    {
        OnPropertyChanged(nameof(FirstRunCompletedStepCount));
        OnPropertyChanged(nameof(FirstRunProgressLabel));
        OnPropertyChanged(nameof(FirstRunSupervisorStepLabel));
        OnPropertyChanged(nameof(FirstRunServicesStepLabel));
        OnPropertyChanged(nameof(FirstRunProjectsStepLabel));
        OnPropertyChanged(nameof(FirstRunSslStepLabel));
        OnPropertyChanged(nameof(FirstRunValidationStepLabel));
    }

    private void ShowGlobalNotification(string level, string message)
    {
        GlobalNotificationTitle = level switch
        {
            "Error" => "Action failed",
            "Warning" => "Needs attention",
            _ => "Locora update"
        };
        GlobalNotificationMessage = message;
        GlobalNotificationSeverity = level switch
        {
            "Error" => InfoBarSeverity.Error,
            "Warning" => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Success
        };
        IsGlobalNotificationOpen = true;
    }

    private static bool ShouldShowGlobalNotification(string level, string message)
    {
        if (level is "Error" or "Warning")
        {
            return true;
        }

        return level == "Info" &&
            !message.StartsWith("Snapshot applied", StringComparison.OrdinalIgnoreCase) &&
            !message.Equals("Locora shell initialized.", StringComparison.OrdinalIgnoreCase);
    }

    private string FormatPermissionDiagnosticLabel(string key, string nameFallback, string summaryFallback)
    {
        var diagnostic = PermissionDiagnostics.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return diagnostic is null
            ? $"{nameFallback}: {summaryFallback}"
            : $"{diagnostic.Name}: {diagnostic.Summary}";
    }

    private async Task LoadDomainSettingsAsync()
    {
        try
        {
            var document = NormalizeProjectSettingsDocument(await ReadProjectSettingsDocumentAsync());
            GeneratedDomainSuffix = document.LocoraProjects.DomainSuffix;
            GeneratedDomainScheme = NormalizeGeneratedDomainScheme(document.LocoraProjects.DefaultScheme);
            DomainSettingsStatus = TryNormalizeGeneratedDomainSettings(out var normalizedSuffix, out var normalizedScheme, out var validationMessage)
                ? $"Loaded project domain defaults: {normalizedScheme}://<project>.{normalizedSuffix}"
                : validationMessage;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load project domain settings.");
            DomainSettingsStatus = $"Could not load project domain settings: {exception.Message}";
        }
    }

    private async Task LoadTerminalCommandsAsync()
    {
        try
        {
            var previousSelectionKey = SelectedTerminalCommand?.Key;
            var document = NormalizeTerminalCommandsDocument(await ReadTerminalCommandsDocumentAsync());
            ReplaceCollection(TerminalCommands, CreateTerminalQuickCommands(document));
            SelectedTerminalCommand = TerminalCommands.FirstOrDefault(command =>
                    command.Key.Equals(previousSelectionKey, StringComparison.OrdinalIgnoreCase)) ??
                TerminalCommands.FirstOrDefault();
            NotifyTerminalCommandCollectionProperties();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load terminal command settings.");
            ReplaceCollection(TerminalCommands, Array.Empty<TerminalQuickCommand>());
            SelectedTerminalCommand = null;
            NotifyTerminalCommandCollectionProperties();
            RecordActivity("Warning", $"Terminal command settings could not be loaded: {exception.Message}");
        }
    }

    private async Task LoadProjectPinsAsync()
    {
        try
        {
            _pinnedProjectKeys = new HashSet<string>(
                await _projectPinStore.LoadPinnedProjectKeysAsync(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load pinned project settings.");
            _pinnedProjectKeys.Clear();
            RecordActivity("Warning", $"Pinned project settings could not be loaded: {exception.Message}");
        }
    }

    private async Task LoadOnboardingStateAsync()
    {
        try
        {
            IsFirstRunOnboardingDismissed = await _onboardingStateStore.LoadFirstRunDismissedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load first-run onboarding state.");
            IsFirstRunOnboardingDismissed = false;
            RecordActivity("Warning", $"First-run onboarding state could not be loaded: {exception.Message}");
        }
    }

    private async Task<ProjectSettingsDocument?> ReadProjectSettingsDocumentAsync()
    {
        var settingsPath = _environmentPaths.ProjectsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<ProjectSettingsDocument>(stream, ProjectSettingsSerializerOptions);
    }

    private async Task<TerminalCommandsDocument?> ReadTerminalCommandsDocumentAsync()
    {
        var settingsPath = _environmentPaths.TerminalCommandsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<TerminalCommandsDocument>(stream, ProjectSettingsSerializerOptions);
    }

    private static ProjectSettingsDocument NormalizeProjectSettingsDocument(ProjectSettingsDocument? document)
    {
        if (document?.LocoraProjects is null)
        {
            return CreateDefaultProjectSettingsDocument();
        }

        var settings = document.LocoraProjects;
        return new ProjectSettingsDocument(
            settings with
            {
                DomainSuffix = string.IsNullOrWhiteSpace(settings.DomainSuffix)
                    ? DefaultGeneratedDomainSuffix
                    : settings.DomainSuffix.Trim(),
                DefaultScheme = NormalizeGeneratedDomainScheme(settings.DefaultScheme),
                IndexFileNames = NormalizeConfiguredNames(settings.IndexFileNames, DefaultIndexFileNames),
                IgnoredDirectoryNames = NormalizeConfiguredNames(settings.IgnoredDirectoryNames, DefaultIgnoredDirectoryNames)
            });
    }

    private static TerminalCommandsDocument NormalizeTerminalCommandsDocument(TerminalCommandsDocument? document)
    {
        if (document?.LocoraTerminal?.Commands is { Count: > 0 })
        {
            return document;
        }

        return CreateDefaultTerminalCommandsDocument();
    }

    private static TerminalCommandsDocument CreateDefaultTerminalCommandsDocument()
    {
        return new TerminalCommandsDocument(
            new TerminalCommandsSection(
                new[]
                {
                    new TerminalCommandSettings("git-status", "Git status", "gst", "Inspect the selected terminal workspace before making changes.", "git status", "any", new[] { "git", "status", "workspace" }),
                    new TerminalCommandSettings("php-version", "PHP version", "phpv", "Print the active PHP runtime version from the injected PATH.", "php -v", "any", new[] { "php", "runtime", "version" }),
                    new TerminalCommandSettings("node-version", "Node.js version", "nodev", "Print the active Node.js runtime version from the injected PATH.", "node --version", "any", new[] { "node", "nodejs", "runtime", "version" }),
                    new TerminalCommandSettings("python-version", "Python version", "pyv", "Print the active Python runtime version from the injected PATH.", "python --version", "any", new[] { "python", "runtime", "version" }),
                    new TerminalCommandSettings("java-version", "Java version", "javav", "Print the active Java runtime version from the injected PATH.", "java -version", "any", new[] { "java", "runtime", "version" }),
                    new TerminalCommandSettings("composer-version", "Composer version", "compv", "Print the active Composer tool version from the injected PATH.", "composer --version", "any", new[] { "composer", "tool", "version" }),
                    new TerminalCommandSettings("composer-install", "Composer install", "cinst", "Install PHP project dependencies in the selected project terminal tab.", "composer install", "project", new[] { "composer", "php", "dependencies", "project" }),
                    new TerminalCommandSettings("npm-install", "NPM install", "npmi", "Install Node.js project dependencies in the selected project terminal tab.", "npm install", "project", new[] { "npm", "node", "dependencies", "project" }),
                    new TerminalCommandSettings("npm-dev", "NPM dev server", "npmdev", "Start the common npm development server in the selected project terminal tab.", "npm run dev", "project", new[] { "npm", "node", "dev", "project" })
                }));
    }

    private static IEnumerable<TerminalQuickCommand> CreateTerminalQuickCommands(TerminalCommandsDocument document)
    {
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in document.LocoraTerminal.Commands)
        {
            var commandText = command.CommandText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commandText))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
                ? commandText
                : command.DisplayName.Trim();
            var key = CreateTerminalCommandKey(command.Key, displayName, command.Alias);
            if (!uniqueKeys.Add(key))
            {
                continue;
            }

            yield return new TerminalQuickCommand(
                key,
                displayName,
                command.Alias?.Trim() ?? string.Empty,
                command.Description?.Trim() ?? string.Empty,
                commandText,
                NormalizeTerminalCommandScope(command.Scope),
                NormalizeConfiguredNames(command.Tags, Array.Empty<string>()));
        }
    }

    private static string CreateTerminalCommandKey(string? key, string displayName, string? alias)
    {
        var preferred = string.IsNullOrWhiteSpace(key)
            ? string.IsNullOrWhiteSpace(alias)
                ? displayName
                : alias
            : key;
        var builder = new StringBuilder(preferred.Length);

        foreach (var character in preferred.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? "command"
            : normalized;
    }

    private string ResolveTerminalCommandText(TerminalQuickCommand command, TerminalSession? session = null)
    {
        var targetSession = session ?? SelectedTerminalSession;
        var launchProfile = targetSession?.LaunchProfile ?? CreateRootTerminalLaunchProfile();
        var environmentVariables = launchProfile.EnvironmentVariables;
        var workingDirectory = targetSession?.WorkingDirectory ?? launchProfile.WorkingDirectory;
        var projectRoot = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_ROOT", workingDirectory);
        var projectName = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_NAME", Path.GetFileName(projectRoot));
        var appUrl = GetTerminalLaunchProfileValue(launchProfile, "APP_URL", string.Empty);
        var projectUrl = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_URL", appUrl);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{aliasesRoot}"] = _environmentPaths.AliasesRoot,
            ["{appUrl}"] = appUrl,
            ["{binRoot}"] = _environmentPaths.BinRoot,
            ["{configRoot}"] = _environmentPaths.ConfigRoot,
            ["{cwd}"] = workingDirectory,
            ["{dataRoot}"] = _environmentPaths.DataRoot,
            ["{locoraProfile}"] = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROFILE", _lastSnapshot?.ActiveProfile ?? "Bootstrap"),
            ["{locoraRoot}"] = _environmentPaths.AppRoot,
            ["{logsRoot}"] = _environmentPaths.LogsRoot,
            ["{projectName}"] = projectName,
            ["{projectRoot}"] = projectRoot,
            ["{projectUrl}"] = projectUrl,
            ["{sessionTitle}"] = targetSession?.Title ?? "Locora root",
            ["{tempRoot}"] = _environmentPaths.TempRoot,
            ["{userRoot}"] = _environmentPaths.UserRoot,
            ["{workingDirectory}"] = workingDirectory
        };

        var resolved = command.CommandText;
        foreach (var pair in replacements)
        {
            resolved = resolved.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        resolved = Regex.Replace(
            resolved,
            @"\{env:([A-Za-z0-9_]+)\}",
            match =>
            {
                var key = match.Groups[1].Value;
                return environmentVariables.TryGetValue(key, out var value)
                    ? value
                    : Environment.GetEnvironmentVariable(key) ?? match.Value;
            },
            RegexOptions.IgnoreCase);

        return resolved.Trim();
    }

    private static string GetTerminalLaunchProfileValue(ProjectTerminalLaunchProfile launchProfile, string key, string fallback)
    {
        return launchProfile.EnvironmentVariables.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string NormalizeTerminalCommandScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "any";
        }

        return scope.Trim().ToLowerInvariant() switch
        {
            "project" => "project",
            "root" => "root",
            _ => "any"
        };
    }

    private static string NormalizeGeneratedDomainScheme(string? scheme)
    {
        return string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
    }

    private bool TryNormalizeGeneratedDomainSettings(out string normalizedSuffix, out string normalizedScheme, out string validationMessage)
    {
        normalizedScheme = NormalizeGeneratedDomainScheme(GeneratedDomainScheme);
        normalizedSuffix = string.Empty;

        var candidate = (GeneratedDomainSuffix ?? string.Empty).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            validationMessage = "Enter a domain suffix like test or dev.local.";
            return false;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            validationMessage = "Enter only the suffix or hostname ending, not a full URL.";
            return false;
        }

        if (candidate.IndexOfAny(new[] { '/', '\\', '?', '#', ':' }) >= 0 || candidate.Any(char.IsWhiteSpace))
        {
            validationMessage = "The domain suffix cannot contain spaces, slashes, ports, or URL fragments.";
            return false;
        }

        candidate = candidate.ToLowerInvariant();
        if (Uri.CheckHostName(candidate) == UriHostNameType.Unknown)
        {
            validationMessage = "Enter a valid suffix like test, local, or dev.local.";
            return false;
        }

        normalizedSuffix = candidate;
        validationMessage = string.Empty;
        return true;
    }

    private static ProjectSettingsDocument CreateDefaultProjectSettingsDocument()
    {
        return new ProjectSettingsDocument(
            new ProjectSettingsSection(
                EnableAutoDiscovery: true,
                GenerateNginxVHosts: true,
                GenerateApacheVHosts: true,
                GenerateHostsPreview: true,
                DomainSuffix: DefaultGeneratedDomainSuffix,
                DefaultScheme: DefaultGeneratedDomainScheme,
                IndexFileNames: DefaultIndexFileNames,
                IgnoredDirectoryNames: DefaultIgnoredDirectoryNames));
    }

    private string CreateProjectKey(string projectPath)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var fullProjectRoot = Path.GetFullPath(_environmentPaths.ProjectRoot);
        var relativePath = Path.GetRelativePath(fullProjectRoot, fullProjectPath).Replace('\\', '/');

        return relativePath.StartsWith("..", StringComparison.Ordinal)
            ? fullProjectPath.Replace('\\', '/')
            : relativePath;
    }

    private static IReadOnlyList<string> NormalizeConfiguredNames(IReadOnlyList<string>? values, IReadOnlyList<string> defaults)
    {
        var normalized = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? normalized : defaults;
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

    private DatabaseConnectionDetails LoadDatabaseConnectionDetails(ServiceStatusCard service)
    {
        var isPostgreSql = service.Key.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("postgres", StringComparison.OrdinalIgnoreCase);
        var configFolderName = isPostgreSql ? "postgresql" : "mariadb";
        var defaultPort = isPostgreSql ? 5432 : 3306;
        var detailsPath = Path.Combine(_environmentPaths.ConfigRoot, configFolderName, "connection-details.md");
        var detailsExists = File.Exists(detailsPath);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (detailsExists)
        {
            foreach (var line in File.ReadLines(detailsPath))
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

        var port = TryParsePort(values.TryGetValue("Port", out var portText) ? portText : null, defaultPort);
        var url = values.TryGetValue("URL", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : isPostgreSql
                ? $"postgresql://postgres@127.0.0.1:{port}/postgres"
                : $"mysql://127.0.0.1:{port}/";

        return new DatabaseConnectionDetails(
            service.Name,
            values.TryGetValue("Host", out var host) && !string.IsNullOrWhiteSpace(host) ? host : "127.0.0.1",
            port,
            values.TryGetValue("Database", out var database) && !string.IsNullOrWhiteSpace(database)
                ? database
                : isPostgreSql
                    ? "postgres"
                    : "(choose your database)",
            values.TryGetValue("Username", out var username) && !string.IsNullOrWhiteSpace(username)
                ? username
                : isPostgreSql
                    ? "postgres"
                    : "root",
            values.TryGetValue("Password", out var password) && !string.IsNullOrWhiteSpace(password)
                ? password
                : isPostgreSql
                    ? "(none, local trust auth)"
                    : "(set by your local MariaDB initialization)",
            url,
            Path.Combine(_environmentPaths.ConfigRoot, configFolderName),
            detailsPath,
            detailsExists);
    }

    private string BuildDatabaseConnectionSnippet(ServiceStatusCard service)
    {
        var details = LoadDatabaseConnectionDetails(service);
        var builder = new StringBuilder();
        builder.AppendLine($"DB_HOST={details.Host}");
        builder.AppendLine($"DB_PORT={details.Port}");
        builder.AppendLine(details.Database.StartsWith("(", StringComparison.Ordinal) ? "DB_DATABASE=" : $"DB_DATABASE={details.Database}");
        builder.AppendLine(details.Username.StartsWith("(", StringComparison.Ordinal) ? "DB_USERNAME=" : $"DB_USERNAME={details.Username}");
        builder.AppendLine(details.Password.StartsWith("(", StringComparison.Ordinal) ? "DB_PASSWORD=" : $"DB_PASSWORD={details.Password}");
        builder.AppendLine($"DB_URL={details.Url}");
        builder.AppendLine($"DB_DETAILS={details.ConnectionDetailsPath}");
        return builder.ToString();
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

    private static bool TryResolveShellContextTarget(
        string targetPath,
        out string resolvedTargetPath,
        out string resolvedFolderPath,
        out string errorMessage)
    {
        resolvedTargetPath = string.Empty;
        resolvedFolderPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            errorMessage = "Explorer context target was empty.";
            return false;
        }

        resolvedTargetPath = Path.GetFullPath(targetPath);
        if (Directory.Exists(resolvedTargetPath))
        {
            resolvedFolderPath = resolvedTargetPath;
            return true;
        }

        if (File.Exists(resolvedTargetPath))
        {
            resolvedFolderPath = Path.GetDirectoryName(resolvedTargetPath) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(resolvedFolderPath);
        }

        errorMessage = $"Explorer context target does not exist: {resolvedTargetPath}";
        return false;
    }

    private static string CreateShellContextTerminalTitle(string folderPath)
    {
        var directoryName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(directoryName)
            ? "Explorer folder"
            : $"Explorer: {directoryName}";
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

    private sealed record DatabaseConnectionDetails(
        string DisplayName,
        string Host,
        int Port,
        string Database,
        string Username,
        string Password,
        string Url,
        string ConfigRoot,
        string ConnectionDetailsPath,
        bool ConnectionDetailsExists);

    private sealed record ProjectSettingsDocument(ProjectSettingsSection LocoraProjects);

    private sealed record TerminalCommandsDocument(TerminalCommandsSection LocoraTerminal);

    private sealed record TerminalCommandsSection(IReadOnlyList<TerminalCommandSettings> Commands);

    private sealed record TerminalCommandSettings(
        string Key,
        string DisplayName,
        string Alias,
        string Description,
        string CommandText,
        string Scope,
        IReadOnlyList<string> Tags);

    private sealed record ProjectSettingsSection(
        bool EnableAutoDiscovery,
        bool GenerateNginxVHosts,
        bool GenerateApacheVHosts,
        bool GenerateHostsPreview,
        string DomainSuffix,
        string DefaultScheme,
        IReadOnlyList<string> IndexFileNames,
        IReadOnlyList<string> IgnoredDirectoryNames);

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
