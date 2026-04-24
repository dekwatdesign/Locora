using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
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
    private static readonly Regex PublicShareUrlRegex = new(@"https?://[^\s<>'""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IWorkbenchService _workbenchService;
    private readonly IProjectActionLauncher _projectActionLauncher;
    private readonly ITerminalSessionService _terminalSessionService;
    private readonly IClipboardService _clipboardService;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IProjectPinStore _projectPinStore;
    private readonly IOnboardingStateStore _onboardingStateStore;
    private readonly IShellContextMenuRegistrationService _shellContextMenuRegistrationService;
    private readonly IUserEnvironmentChangeService _userEnvironmentChangeService;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IPortableDistributionService _portableDistributionService;
    private readonly IUserConfirmationService _userConfirmationService;
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;
    private EnvironmentSnapshot? _lastSnapshot;
    private HashSet<string> _pinnedProjectKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShareUrlSessionCard> _shareSessionsByTerminalId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shareMonitoredTerminalIds = new(StringComparer.OrdinalIgnoreCase);
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
    private bool _externalAccessConsentAccepted;
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
    private string _newStackProfileName = $"Saved Environment {DateTime.Now:yyyyMMdd HHmm}";
    private string _activeEnvironmentSaveStatus = "Save the current running services and active package selections as a reusable stack profile.";
    private string _profileTransferPath = string.Empty;
    private string _profileTransferStatus = "Export profiles to a portable JSON file or import profiles from an existing export.";
    private string _configBackupStatus = "Back up usr/config before migrations, manual edits, or risky service changes.";
    private string _latestConfigBackupPath = string.Empty;
    private string _configRestorePath = string.Empty;
    private string _configRestoreStatus = "Restore a Locora configuration backup zip when you need to roll back usr/config.";
    private string _laragonRootPath = ResolveDefaultLaragonRootPath();
    private string _laragonImportStatus = "Preview a Laragon root to import projects from its www folder into Locora project discovery.";
    private string _laragonImportSummary = "No Laragon import preview has been generated.";
    private TerminalSession? _selectedTerminalSession;
    private TerminalQuickCommand? _selectedTerminalCommand;
    private string _terminalInputText = string.Empty;
    private string _customToolsStatus = "Custom tools are loaded from custom-tools.json.";
    private string _localTunnelsStatus = "Local tunnel profiles are loaded from local-tunnels.json.";
    private string _shellIntegrationRootPath = string.Empty;
    private string _shellContextMenuInstallFilePath = string.Empty;
    private string _shellContextMenuUninstallFilePath = string.Empty;
    private string _shellContextMenuReadmeFilePath = string.Empty;
    private string _shellContextMenuExecutablePath = string.Empty;
    private string _userEnvironmentStatus = "Generated CurrentUser environment scripts let you opt in to persistent Locora variables without changing system PATH.";
    private string _userEnvironmentApplyScriptPath = string.Empty;
    private string _userEnvironmentRemoveScriptPath = string.Empty;
    private string _userEnvironmentManifestPath = string.Empty;
    private string _appUpdateState = "Not checked";
    private string _appUpdateSummary = "App update checks have not run in this session.";
    private string _appUpdateDetails = "Configure a release manifest in appsettings.json, then run Check for Updates.";
    private string _appUpdateCurrentVersion = string.Empty;
    private string _appUpdateLatestVersion = string.Empty;
    private string _appUpdateCheckedAtLabel = "Never";
    private string _appUpdateReleasePageUri = string.Empty;
    private string _appUpdateDownloadUri = string.Empty;
    private string _appUpdateSha256 = string.Empty;
    private string _portableDistributionStatus = "Portable distribution files have not been generated in this session.";
    private string _portableDistributionScriptPath = string.Empty;
    private string _portableDistributionPlanPath = string.Empty;
    private string _portableDistributionReadmePath = string.Empty;
    private string _portableDistributionManifestTemplatePath = string.Empty;

    public MainWindowViewModel(
        IWorkbenchService workbenchService,
        IProjectActionLauncher projectActionLauncher,
        ITerminalSessionService terminalSessionService,
        IClipboardService clipboardService,
        IDiagnosticReportService diagnosticReportService,
        IProjectPinStore projectPinStore,
        IOnboardingStateStore onboardingStateStore,
        IShellContextMenuRegistrationService shellContextMenuRegistrationService,
        IUserEnvironmentChangeService userEnvironmentChangeService,
        IAppUpdateService appUpdateService,
        IPortableDistributionService portableDistributionService,
        IUserConfirmationService userConfirmationService,
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
        _userEnvironmentChangeService = userEnvironmentChangeService;
        _appUpdateService = appUpdateService;
        _portableDistributionService = portableDistributionService;
        _userConfirmationService = userConfirmationService;
        _environmentPaths = environmentPaths;
        _settings = settings.Value;
        _logger = logger;
        _profileTransferPath = Path.Combine(_environmentPaths.ProfilesRoot, "profiles-export.json");

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

        try
        {
            RefreshUserEnvironmentChangeFilesCore();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "User environment change files could not be generated during startup.");
            UserEnvironmentStatus = $"Environment script generation needs attention: {exception.Message}";
            UserEnvironmentApplyScriptPath = _environmentPaths.UserEnvironmentApplyScriptFile;
            UserEnvironmentRemoveScriptPath = _environmentPaths.UserEnvironmentRemoveScriptFile;
            UserEnvironmentManifestPath = _environmentPaths.UserEnvironmentManifestFile;
        }

        Services = new ObservableCollection<ServiceStatusCard>();
        ServicePresets = new ObservableCollection<ServicePresetCard>();
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
        CustomTools = new ObservableCollection<CustomToolMenuItem>();
        LocalTunnels = new ObservableCollection<LocalTunnelProfileCard>();
        ShareUrlSessions = new ObservableCollection<ShareUrlSessionCard>();
        NetworkGuidance = new ObservableCollection<NetworkGuidanceCard>();
        LaragonImportProjects = new ObservableCollection<LaragonImportProjectCard>();
        TerminalSessions = _terminalSessionService.Sessions;
        TerminalSessions.CollectionChanged += OnTerminalSessionsChanged;

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunAction);
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, CanRunAction);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, CanRunAction);
        SaveDomainSettingsCommand = new AsyncRelayCommand(SaveDomainSettingsAsync, CanSaveDomainSettings);
        StartServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StartServiceAsync, CanRunServiceAction);
        StopServiceCommand = new AsyncRelayCommand<ServiceStatusCard>(StopServiceAsync, CanRunServiceAction);
        StartServicePresetCommand = new AsyncRelayCommand<ServicePresetCard>(StartServicePresetAsync, CanRunServicePresetAction);
        StopServicePresetCommand = new AsyncRelayCommand<ServicePresetCard>(StopServicePresetAsync, CanRunServicePresetAction);
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
        BackupConfigCommand = new AsyncRelayCommand(BackupConfigAsync, CanRunAction);
        RestoreConfigCommand = new AsyncRelayCommand(RestoreConfigAsync, CanRestoreConfig);
        OpenConfigBackupRootCommand = new RelayCommand(OpenConfigBackupRoot, CanUseDesktopPathAction);
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
        SaveActiveEnvironmentCommand = new AsyncRelayCommand(SaveActiveEnvironmentAsync, CanSaveActiveEnvironment);
        ExportStackProfilesCommand = new AsyncRelayCommand(ExportStackProfilesAsync, CanTransferStackProfiles);
        ImportStackProfilesCommand = new AsyncRelayCommand(ImportStackProfilesAsync, CanTransferStackProfiles);
        PreviewLaragonImportCommand = new AsyncRelayCommand(PreviewLaragonImportAsync, CanUseLaragonImport);
        ImportLaragonProjectsCommand = new AsyncRelayCommand(ImportLaragonProjectsAsync, CanUseLaragonImport);
        OpenLaragonRootCommand = new RelayCommand(OpenLaragonRoot, CanOpenLaragonRoot);
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
        RunCustomToolCommand = new RelayCommand<CustomToolMenuItem>(RunCustomTool, CanRunCustomTool);
        ReloadCustomToolsCommand = new AsyncRelayCommand(ReloadCustomToolsAsync, CanRunAction);
        OpenCustomToolsSettingsFileCommand = new RelayCommand(OpenCustomToolsSettingsFile, CanUseDesktopPathAction);
        StartLocalTunnelCommand = new RelayCommand<LocalTunnelProfileCard>(StartLocalTunnel, CanStartLocalTunnel);
        ReloadLocalTunnelsCommand = new AsyncRelayCommand(ReloadLocalTunnelsAsync, CanRunAction);
        OpenLocalTunnelsSettingsFileCommand = new RelayCommand(OpenLocalTunnelsSettingsFile, CanUseDesktopPathAction);
        CopyShareUrlCommand = new RelayCommand<ShareUrlSessionCard>(CopyShareUrl, CanUseShareUrl);
        OpenShareUrlCommand = new RelayCommand<ShareUrlSessionCard>(OpenShareUrl, CanUseShareUrl);
        StopShareUrlSessionCommand = new RelayCommand<ShareUrlSessionCard>(StopShareUrlSession, CanStopShareUrlSession);
        ClearStoppedShareUrlsCommand = new RelayCommand(ClearStoppedShareUrls, CanClearStoppedShareUrls);
        OpenAliasesRootCommand = new RelayCommand(OpenAliasesRoot, CanUseDesktopPathAction);
        OpenProfilesRootCommand = new RelayCommand(OpenProfilesRoot, CanUseDesktopPathAction);
        OpenEnvironmentRootInEditorCommand = new RelayCommand(OpenEnvironmentRootInEditor, CanUseDesktopPathAction);
        OpenProjectRootInEditorCommand = new RelayCommand(OpenProjectRootInEditor, CanUseDesktopPathAction);
        RefreshShellContextMenuFilesCommand = new RelayCommand(RefreshShellContextMenuFiles, CanUseDesktopPathAction);
        OpenShellIntegrationRootCommand = new RelayCommand(OpenShellIntegrationRoot, CanUseDesktopPathAction);
        OpenShellContextMenuInstallFileCommand = new RelayCommand(OpenShellContextMenuInstallFile, CanUseDesktopPathAction);
        OpenShellContextMenuUninstallFileCommand = new RelayCommand(OpenShellContextMenuUninstallFile, CanUseDesktopPathAction);
        RefreshUserEnvironmentFilesCommand = new RelayCommand(RefreshUserEnvironmentFiles, CanUseDesktopPathAction);
        OpenUserEnvironmentApplyScriptCommand = new RelayCommand(OpenUserEnvironmentApplyScript, CanUseDesktopPathAction);
        OpenUserEnvironmentRemoveScriptCommand = new RelayCommand(OpenUserEnvironmentRemoveScript, CanUseDesktopPathAction);
        OpenUserEnvironmentManifestCommand = new RelayCommand(OpenUserEnvironmentManifest, CanUseDesktopPathAction);
        CheckForAppUpdatesCommand = new AsyncRelayCommand(CheckForAppUpdatesAsync, CanRunAction);
        OpenAppUpdateReleasePageCommand = new RelayCommand(OpenAppUpdateReleasePage, CanOpenAppUpdateReleasePage);
        OpenAppUpdateDownloadCommand = new RelayCommand(OpenAppUpdateDownload, CanOpenAppUpdateDownload);
        OpenAppUpdatePlanCommand = new RelayCommand(OpenAppUpdatePlan, CanOpenAppUpdatePlan);
        OpenAppUpdateManifestCacheCommand = new RelayCommand(OpenAppUpdateManifestCache, CanOpenAppUpdateManifestCache);
        OpenAppUpdateManifestExampleCommand = new RelayCommand(OpenAppUpdateManifestExample, CanOpenAppUpdateManifestExample);
        RefreshPortableDistributionFilesCommand = new RelayCommand(RefreshPortableDistributionFiles, CanUseDesktopPathAction);
        OpenPortableDistributionRootCommand = new RelayCommand(OpenPortableDistributionRoot, CanUseDesktopPathAction);
        OpenPortableDistributionArtifactsRootCommand = new RelayCommand(OpenPortableDistributionArtifactsRoot, CanUseDesktopPathAction);
        OpenPortableDistributionScriptCommand = new RelayCommand(OpenPortableDistributionScript, CanUseDesktopPathAction);
        OpenPortableDistributionPlanCommand = new RelayCommand(OpenPortableDistributionPlan, CanUseDesktopPathAction);
        OpenPortableDistributionReadmeCommand = new RelayCommand(OpenPortableDistributionReadme, CanUseDesktopPathAction);
        OpenPortableDistributionManifestTemplateCommand = new RelayCommand(OpenPortableDistributionManifestTemplate, CanUseDesktopPathAction);

        try
        {
            ApplyAppUpdateStatus(_appUpdateService.GetCurrentStatus());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "App update status could not be initialized.");
            AppUpdateState = "Unavailable";
            AppUpdateSummary = $"App update status could not be initialized: {exception.Message}";
            AppUpdateDetails = "Open appsettings.json and verify the Updates section.";
        }

        try
        {
            RefreshPortableDistributionFilesCore();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Portable distribution files could not be generated during startup.");
            PortableDistributionStatus = $"Portable distribution files need attention: {exception.Message}";
            PortableDistributionScriptPath = _environmentPaths.PortableDistributionScriptFile;
            PortableDistributionPlanPath = _environmentPaths.PortableDistributionPlanFile;
            PortableDistributionReadmePath = _environmentPaths.PortableDistributionReadmeFile;
            PortableDistributionManifestTemplatePath = _environmentPaths.PortableDistributionManifestTemplateFile;
        }
    }

    public event EventHandler<string>? NavigationRequested;

    public ObservableCollection<ServiceStatusCard> Services { get; }

    public ObservableCollection<ServicePresetCard> ServicePresets { get; }

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

    public ObservableCollection<CustomToolMenuItem> CustomTools { get; }

    public ObservableCollection<LocalTunnelProfileCard> LocalTunnels { get; }

    public ObservableCollection<ShareUrlSessionCard> ShareUrlSessions { get; }

    public ObservableCollection<NetworkGuidanceCard> NetworkGuidance { get; }

    public ObservableCollection<LaragonImportProjectCard> LaragonImportProjects { get; }

    public ObservableCollection<TerminalSession> TerminalSessions { get; }

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand StartAllCommand { get; }

    public IAsyncRelayCommand StopAllCommand { get; }

    public IAsyncRelayCommand SaveDomainSettingsCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StartServiceCommand { get; }

    public IAsyncRelayCommand<ServiceStatusCard> StopServiceCommand { get; }

    public IAsyncRelayCommand<ServicePresetCard> StartServicePresetCommand { get; }

    public IAsyncRelayCommand<ServicePresetCard> StopServicePresetCommand { get; }

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

    public IAsyncRelayCommand BackupConfigCommand { get; }

    public IAsyncRelayCommand RestoreConfigCommand { get; }

    public IRelayCommand OpenConfigBackupRootCommand { get; }

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

    public IAsyncRelayCommand SaveActiveEnvironmentCommand { get; }

    public IAsyncRelayCommand ExportStackProfilesCommand { get; }

    public IAsyncRelayCommand ImportStackProfilesCommand { get; }

    public IAsyncRelayCommand PreviewLaragonImportCommand { get; }

    public IAsyncRelayCommand ImportLaragonProjectsCommand { get; }

    public IRelayCommand OpenLaragonRootCommand { get; }

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

    public IRelayCommand<CustomToolMenuItem> RunCustomToolCommand { get; }

    public IAsyncRelayCommand ReloadCustomToolsCommand { get; }

    public IRelayCommand OpenCustomToolsSettingsFileCommand { get; }

    public IRelayCommand<LocalTunnelProfileCard> StartLocalTunnelCommand { get; }

    public IAsyncRelayCommand ReloadLocalTunnelsCommand { get; }

    public IRelayCommand OpenLocalTunnelsSettingsFileCommand { get; }

    public IRelayCommand<ShareUrlSessionCard> CopyShareUrlCommand { get; }

    public IRelayCommand<ShareUrlSessionCard> OpenShareUrlCommand { get; }

    public IRelayCommand<ShareUrlSessionCard> StopShareUrlSessionCommand { get; }

    public IRelayCommand ClearStoppedShareUrlsCommand { get; }

    public IRelayCommand OpenAliasesRootCommand { get; }

    public IRelayCommand OpenProfilesRootCommand { get; }

    public IRelayCommand OpenEnvironmentRootInEditorCommand { get; }

    public IRelayCommand OpenProjectRootInEditorCommand { get; }

    public IRelayCommand RefreshShellContextMenuFilesCommand { get; }

    public IRelayCommand OpenShellIntegrationRootCommand { get; }

    public IRelayCommand OpenShellContextMenuInstallFileCommand { get; }

    public IRelayCommand OpenShellContextMenuUninstallFileCommand { get; }

    public IRelayCommand RefreshUserEnvironmentFilesCommand { get; }

    public IRelayCommand OpenUserEnvironmentApplyScriptCommand { get; }

    public IRelayCommand OpenUserEnvironmentRemoveScriptCommand { get; }

    public IRelayCommand OpenUserEnvironmentManifestCommand { get; }

    public IAsyncRelayCommand CheckForAppUpdatesCommand { get; }

    public IRelayCommand OpenAppUpdateReleasePageCommand { get; }

    public IRelayCommand OpenAppUpdateDownloadCommand { get; }

    public IRelayCommand OpenAppUpdatePlanCommand { get; }

    public IRelayCommand OpenAppUpdateManifestCacheCommand { get; }

    public IRelayCommand OpenAppUpdateManifestExampleCommand { get; }

    public IRelayCommand RefreshPortableDistributionFilesCommand { get; }

    public IRelayCommand OpenPortableDistributionRootCommand { get; }

    public IRelayCommand OpenPortableDistributionArtifactsRootCommand { get; }

    public IRelayCommand OpenPortableDistributionScriptCommand { get; }

    public IRelayCommand OpenPortableDistributionPlanCommand { get; }

    public IRelayCommand OpenPortableDistributionReadmeCommand { get; }

    public IRelayCommand OpenPortableDistributionManifestTemplateCommand { get; }

    public string EnvironmentRoot { get; }

    public string ConfigRoot { get; }

    public string ConfigBackupRootPath => _environmentPaths.ConfigBackupRoot;

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

    public string UserEnvironmentStatus
    {
        get => _userEnvironmentStatus;
        private set => SetProperty(ref _userEnvironmentStatus, value);
    }

    public string UserEnvironmentApplyScriptPath
    {
        get => _userEnvironmentApplyScriptPath;
        private set => SetProperty(ref _userEnvironmentApplyScriptPath, value);
    }

    public string UserEnvironmentRemoveScriptPath
    {
        get => _userEnvironmentRemoveScriptPath;
        private set => SetProperty(ref _userEnvironmentRemoveScriptPath, value);
    }

    public string UserEnvironmentManifestPath
    {
        get => _userEnvironmentManifestPath;
        private set => SetProperty(ref _userEnvironmentManifestPath, value);
    }

    public string UserEnvironmentBackupRootPath => _environmentPaths.UserEnvironmentBackupRoot;

    public string UserEnvironmentManagedPathEntriesLabel => $"Managed PATH entry: {AliasesRootPath}. Active runtime and tool paths stay session-scoped in built-in terminals.";

    public string UserEnvironmentManagedVariablesLabel => "Managed variables: LOCORA_ROOT, LOCORA_ALIASES_ROOT, LOCORA_BIN, LOCORA_CONFIG, LOCORA_DATA, LOCORA_LOGS, LOCORA_TEMP, LOCORA_PACKAGE_CACHE.";

    public string AppUpdateState
    {
        get => _appUpdateState;
        private set => SetProperty(ref _appUpdateState, value);
    }

    public string AppUpdateSummary
    {
        get => _appUpdateSummary;
        private set => SetProperty(ref _appUpdateSummary, value);
    }

    public string AppUpdateDetails
    {
        get => _appUpdateDetails;
        private set => SetProperty(ref _appUpdateDetails, value);
    }

    public string AppUpdateCurrentVersion
    {
        get => _appUpdateCurrentVersion;
        private set => SetProperty(ref _appUpdateCurrentVersion, value);
    }

    public string DeveloperName => ResolveDeveloperName();

    public string AppUpdateLatestVersion
    {
        get => _appUpdateLatestVersion;
        private set => SetProperty(ref _appUpdateLatestVersion, value);
    }

    public string AppUpdateChannel => string.IsNullOrWhiteSpace(_settings.Updates.Channel)
        ? "stable"
        : _settings.Updates.Channel;

    public string AppUpdateManifestUri => string.IsNullOrWhiteSpace(_settings.Updates.ManifestUri)
        ? "(not configured)"
        : _settings.Updates.ManifestUri;

    public string AppUpdateCheckPolicy => $"Check on startup: {(_settings.Updates.CheckOnStartup ? "enabled" : "disabled")}. Prerelease builds: {(_settings.Updates.AllowPrerelease ? "included" : "skipped")}. Timeout: {Math.Max(1000, _settings.Updates.CheckTimeoutMs)} ms.";

    public string AppUpdateCheckedAtLabel
    {
        get => _appUpdateCheckedAtLabel;
        private set => SetProperty(ref _appUpdateCheckedAtLabel, value);
    }

    public string AppUpdateReleasePageUri
    {
        get => _appUpdateReleasePageUri;
        private set => SetProperty(ref _appUpdateReleasePageUri, value);
    }

    public string AppUpdateDownloadUri
    {
        get => _appUpdateDownloadUri;
        private set => SetProperty(ref _appUpdateDownloadUri, value);
    }

    public string AppUpdateSha256
    {
        get => _appUpdateSha256;
        private set => SetProperty(ref _appUpdateSha256, value);
    }

    public string AppUpdateRootPath => _environmentPaths.AppUpdateRoot;

    public string AppUpdateDownloadRootPath => _environmentPaths.AppUpdateDownloadRoot;

    public string AppUpdatePlanPath => _environmentPaths.AppUpdatePlanFile;

    public string AppUpdateManifestCachePath => _environmentPaths.AppUpdateManifestCacheFile;

    public string AppUpdateManifestExamplePath => _environmentPaths.AppUpdateManifestExampleFile;

    public string PortableDistributionStatus
    {
        get => _portableDistributionStatus;
        private set => SetProperty(ref _portableDistributionStatus, value);
    }

    public string PortableDistributionModeLabel => $"Mode: portable ZIP, {_settings.Distribution.Configuration} configuration, {_settings.Distribution.RuntimeIdentifier} runtime identifier.";

    public string PortableDistributionScopeLabel => $"Includes runtime binaries: {(_settings.Distribution.IncludeRuntimeBinaries ? "yes" : "no")}. Includes package cache: {(_settings.Distribution.IncludePackageCache ? "yes" : "no")}. Includes user data: {(_settings.Distribution.IncludeUserData ? "yes" : "no")}. Release manifest: {(_settings.Distribution.CreateReleaseManifest ? "generated" : "manual")}.";

    public string PortableDistributionRootPath => _environmentPaths.PortableDistributionRoot;

    public string PortableDistributionArtifactsRootPath => _environmentPaths.PortableDistributionArtifactsRoot;

    public string PortableDistributionScriptPath
    {
        get => _portableDistributionScriptPath;
        private set => SetProperty(ref _portableDistributionScriptPath, value);
    }

    public string PortableDistributionPlanPath
    {
        get => _portableDistributionPlanPath;
        private set => SetProperty(ref _portableDistributionPlanPath, value);
    }

    public string PortableDistributionReadmePath
    {
        get => _portableDistributionReadmePath;
        private set => SetProperty(ref _portableDistributionReadmePath, value);
    }

    public string PortableDistributionManifestTemplatePath
    {
        get => _portableDistributionManifestTemplatePath;
        private set => SetProperty(ref _portableDistributionManifestTemplatePath, value);
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

    public bool HasCustomTools => CustomTools.Count > 0;

    public bool ShowCustomToolsEmptyState => !HasCustomTools;

    public bool HasLocalTunnels => LocalTunnels.Count > 0;

    public bool ShowLocalTunnelsEmptyState => !HasLocalTunnels;

    public bool HasShareUrlSessions => ShareUrlSessions.Count > 0;

    public bool ShowShareUrlSessionsEmptyState => !HasShareUrlSessions;

    public bool HasNetworkGuidance => NetworkGuidance.Count > 0;

    public bool ShowNetworkGuidanceEmptyState => !HasNetworkGuidance;

    public string NetworkGuidanceSummary
    {
        get
        {
            var services = _lastSnapshot?.Services ?? Array.Empty<ServiceDescriptor>();
            var servicePortCount = services.Count(service => service.Port is > 0);
            var enabledTunnelCount = LocalTunnels.Count(profile => profile.IsEnabled);

            if (servicePortCount == 0 && enabledTunnelCount == 0)
            {
                return "Network guidance will update after services and local tunnel profiles are loaded.";
            }

            var portLabel = servicePortCount == 1 ? "configured service port" : "configured service ports";
            var tunnelLabel = enabledTunnelCount == 1 ? "enabled tunnel profile" : "enabled tunnel profiles";
            return $"{servicePortCount} {portLabel} and {enabledTunnelCount} {tunnelLabel} covered. Keep inbound firewall rules limited to trusted private networks.";
        }
    }

    public string ShareUrlLifecycleSummary
    {
        get
        {
            if (ShareUrlSessions.Count == 0)
            {
                return "No share URL sessions are active.";
            }

            var activeCount = ShareUrlSessions.Count(session => session.IsRunning);
            var urlCount = ShareUrlSessions.Count(session => session.HasPublicUrl);
            return $"{activeCount} share session{(activeCount == 1 ? string.Empty : "s")} running, {urlCount} public URL{(urlCount == 1 ? string.Empty : "s")} detected.";
        }
    }

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

    public string CustomToolsStatus
    {
        get => _customToolsStatus;
        private set => SetProperty(ref _customToolsStatus, value);
    }

    public string LocalTunnelsStatus
    {
        get => _localTunnelsStatus;
        private set => SetProperty(ref _localTunnelsStatus, value);
    }

    public string ProjectsSettingsFilePath => _environmentPaths.ProjectsSettingsFile;

    public string ProfilesSettingsFilePath => _environmentPaths.ProfilesSettingsFile;

    public string ProfilesRootPath => _environmentPaths.ProfilesRoot;

    public string OnboardingSettingsFilePath => _environmentPaths.OnboardingSettingsFile;

    public string AppSettingsFilePath => _environmentPaths.AppSettingsFile;

    public string SupervisorSettingsFilePath => _environmentPaths.SupervisorSettingsFile;

    public string ServicesSettingsFilePath => _environmentPaths.ServicesSettingsFile;

    public string PackageSourcesSettingsFilePath => _environmentPaths.PackageSourcesSettingsFile;

    public string PackagesLockSettingsFilePath => _environmentPaths.PackagesLockSettingsFile;

    public string TerminalCommandsSettingsFilePath => _environmentPaths.TerminalCommandsSettingsFile;

    public string CustomToolsSettingsFilePath => _environmentPaths.CustomToolsSettingsFile;

    public string LocalTunnelsSettingsFilePath => _environmentPaths.LocalTunnelsSettingsFile;

    public string AliasesRootPath => _environmentPaths.AliasesRoot;

    public string ProjectPinsSettingsFilePath => _environmentPaths.ProjectPinsSettingsFile;

    public IReadOnlyList<string> DomainSchemes => GeneratedDomainSchemes;

    public string PackageManifestsRootPath => _environmentPaths.PackageManifestsRoot;

    public string PackageCacheRootPath => _environmentPaths.PackageCacheRoot;

    public string RuntimeBackupStatusLabel
    {
        get
        {
            if (_lastSnapshot is null)
            {
                return "Refresh the supervisor snapshot to include runtime and package counts in backup guidance.";
            }

            return $"Current runtime scope: {_activeRuntimePackageCount}/{_runtimePackageCount} active runtimes, {_installedRuntimePackageCount} installed runtimes, {_installedToolPackageCount} installed tools, {_cachedPackageDownloadCount} cached package artifacts.";
        }
    }

    public string RuntimeBackupIncludeLabel => $"Back up configuration archives, service data, package lock state, and package manifests. Include runtime binaries and the package cache when the backup must restore without downloading packages again.";

    public string RuntimeBackupRestoreOrderLabel => "Restore order: stop services, restore the config backup zip, copy service data, install or extract packages, refresh the snapshot, then start services.";

    public string RuntimeBackupSkipLabel => "Skip temporary files. Keep logs only when you are preserving evidence for troubleshooting.";

    public string PortableRelocationValidationStatusLabel
    {
        get
        {
            var validation = FindPortableRelocationValidation();
            return validation is null
                ? "Refresh the supervisor snapshot to validate whether configured paths can survive moving Locora to another folder or drive."
                : $"{validation.State}: {validation.Summary}";
        }
    }

    public string PortableRelocationValidationDetails
    {
        get
        {
            var validation = FindPortableRelocationValidation();
            return validation is null
                ? "The check reviews service definitions, project paths, package manifests, cached artifacts, installed runtime roots, and active tool aliases for absolute or external paths."
                : validation.Details;
        }
    }

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

    public bool HasServicePresets => ServicePresets.Count > 0;

    public bool ShowServicePresetsEmptyState => !HasServicePresets;

    public bool HasProjects => Projects.Count > 0;

    public bool ShowProjectsEmptyState => !HasProjects;

    public bool HasHealthIssues => HealthIssues.Count > 0;

    public bool ShowHealthIssuesEmptyState => !HasHealthIssues;

    public bool HasValidationResults => ValidationResults.Count > 0;

    public bool ShowValidationResultsEmptyState => !HasValidationResults;

    private ValidationResultCard? FindPortableRelocationValidation()
    {
        return ValidationResults.FirstOrDefault(validation =>
            validation.Key.Equals("portable_relocation_readiness", StringComparison.OrdinalIgnoreCase));
    }

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

    public bool ExternalAccessConsentAccepted
    {
        get => _externalAccessConsentAccepted;
        set
        {
            if (SetProperty(ref _externalAccessConsentAccepted, value))
            {
                OnPropertyChanged(nameof(ExternalAccessConsentStatus));
                OnPropertyChanged(nameof(ShowExternalAccessConsentWarning));
                StartLocalTunnelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShowExternalAccessConsentWarning => !ExternalAccessConsentAccepted;

    public string ExternalAccessConsentStatus => ExternalAccessConsentAccepted
        ? "External access is enabled for this app session. Stop tunnel sessions when sharing is no longer needed."
        : "Review the warning and confirm consent before starting a tunnel.";

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
                StartServicePresetCommand.NotifyCanExecuteChanged();
                StopServicePresetCommand.NotifyCanExecuteChanged();
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
                BackupConfigCommand.NotifyCanExecuteChanged();
                RestoreConfigCommand.NotifyCanExecuteChanged();
                OpenConfigBackupRootCommand.NotifyCanExecuteChanged();
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
                RunCustomToolCommand.NotifyCanExecuteChanged();
                ReloadCustomToolsCommand.NotifyCanExecuteChanged();
                OpenCustomToolsSettingsFileCommand.NotifyCanExecuteChanged();
                StartLocalTunnelCommand.NotifyCanExecuteChanged();
                ReloadLocalTunnelsCommand.NotifyCanExecuteChanged();
                OpenLocalTunnelsSettingsFileCommand.NotifyCanExecuteChanged();
                CopyShareUrlCommand.NotifyCanExecuteChanged();
                OpenShareUrlCommand.NotifyCanExecuteChanged();
                StopShareUrlSessionCommand.NotifyCanExecuteChanged();
                ClearStoppedShareUrlsCommand.NotifyCanExecuteChanged();
                OpenAliasesRootCommand.NotifyCanExecuteChanged();
                OpenProfilesRootCommand.NotifyCanExecuteChanged();
                OpenEnvironmentRootInEditorCommand.NotifyCanExecuteChanged();
                OpenProjectRootInEditorCommand.NotifyCanExecuteChanged();
                RefreshShellContextMenuFilesCommand.NotifyCanExecuteChanged();
                OpenShellIntegrationRootCommand.NotifyCanExecuteChanged();
                OpenShellContextMenuInstallFileCommand.NotifyCanExecuteChanged();
                OpenShellContextMenuUninstallFileCommand.NotifyCanExecuteChanged();
                RefreshUserEnvironmentFilesCommand.NotifyCanExecuteChanged();
                OpenUserEnvironmentApplyScriptCommand.NotifyCanExecuteChanged();
                OpenUserEnvironmentRemoveScriptCommand.NotifyCanExecuteChanged();
                OpenUserEnvironmentManifestCommand.NotifyCanExecuteChanged();
                CheckForAppUpdatesCommand.NotifyCanExecuteChanged();
                OpenAppUpdateReleasePageCommand.NotifyCanExecuteChanged();
                OpenAppUpdateDownloadCommand.NotifyCanExecuteChanged();
                OpenAppUpdatePlanCommand.NotifyCanExecuteChanged();
                OpenAppUpdateManifestCacheCommand.NotifyCanExecuteChanged();
                OpenAppUpdateManifestExampleCommand.NotifyCanExecuteChanged();
                RefreshPortableDistributionFilesCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionRootCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionArtifactsRootCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionScriptCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionPlanCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionReadmeCommand.NotifyCanExecuteChanged();
                OpenPortableDistributionManifestTemplateCommand.NotifyCanExecuteChanged();
                OpenPackageManifestsRootCommand.NotifyCanExecuteChanged();
                OpenPackageCacheRootCommand.NotifyCanExecuteChanged();
                SyncPackageDownloadsCommand.NotifyCanExecuteChanged();
                ExtractPackageArchivesCommand.NotifyCanExecuteChanged();
                InstallOrUpdatePackagesCommand.NotifyCanExecuteChanged();
                RemovePackageInstallCommand.NotifyCanExecuteChanged();
                SwitchRuntimeVersionCommand.NotifyCanExecuteChanged();
                SelectStackProfileCommand.NotifyCanExecuteChanged();
                SaveActiveEnvironmentCommand.NotifyCanExecuteChanged();
                ExportStackProfilesCommand.NotifyCanExecuteChanged();
                ImportStackProfilesCommand.NotifyCanExecuteChanged();
                PreviewLaragonImportCommand.NotifyCanExecuteChanged();
                ImportLaragonProjectsCommand.NotifyCanExecuteChanged();
                OpenLaragonRootCommand.NotifyCanExecuteChanged();
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

    public string NewStackProfileName
    {
        get => _newStackProfileName;
        set
        {
            if (SetProperty(ref _newStackProfileName, value))
            {
                SaveActiveEnvironmentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ActiveEnvironmentSaveStatus
    {
        get => _activeEnvironmentSaveStatus;
        private set => SetProperty(ref _activeEnvironmentSaveStatus, value);
    }

    public string ProfileTransferPath
    {
        get => _profileTransferPath;
        set
        {
            if (SetProperty(ref _profileTransferPath, value))
            {
                ExportStackProfilesCommand.NotifyCanExecuteChanged();
                ImportStackProfilesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProfileTransferStatus
    {
        get => _profileTransferStatus;
        private set => SetProperty(ref _profileTransferStatus, value);
    }

    public string ConfigBackupStatus
    {
        get => _configBackupStatus;
        private set => SetProperty(ref _configBackupStatus, value);
    }

    public string LatestConfigBackupPath
    {
        get => _latestConfigBackupPath;
        private set => SetProperty(ref _latestConfigBackupPath, value);
    }

    public string ConfigRestorePath
    {
        get => _configRestorePath;
        set
        {
            if (SetProperty(ref _configRestorePath, value))
            {
                RestoreConfigCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ConfigRestoreStatus
    {
        get => _configRestoreStatus;
        private set => SetProperty(ref _configRestoreStatus, value);
    }

    public string LaragonRootPath
    {
        get => _laragonRootPath;
        set
        {
            if (SetProperty(ref _laragonRootPath, value))
            {
                PreviewLaragonImportCommand.NotifyCanExecuteChanged();
                ImportLaragonProjectsCommand.NotifyCanExecuteChanged();
                OpenLaragonRootCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string LaragonImportStatus
    {
        get => _laragonImportStatus;
        private set => SetProperty(ref _laragonImportStatus, value);
    }

    public string LaragonImportSummary
    {
        get => _laragonImportSummary;
        private set => SetProperty(ref _laragonImportSummary, value);
    }

    public bool HasLaragonImportProjects => LaragonImportProjects.Count > 0;

    public bool ShowLaragonImportEmptyState => !HasLaragonImportProjects;

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
        await LoadCustomToolsAsync();
        await LoadLocalTunnelsAsync();
        if (_settings.Updates.CheckOnStartup)
        {
            await CheckForAppUpdatesCoreAsync();
        }

        await RefreshAsync();
    }

    private bool CanRunAction() => !IsBusy;

    private bool CanSaveDomainSettings()
    {
        return !IsBusy && TryNormalizeGeneratedDomainSettings(out _, out _, out _);
    }

    private bool CanSaveActiveEnvironment()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(NewStackProfileName);
    }

    private bool CanTransferStackProfiles()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ProfileTransferPath);
    }

    private bool CanOpenAppUpdateReleasePage()
    {
        return !IsBusy &&
            Uri.TryCreate(AppUpdateReleasePageUri, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanOpenAppUpdateDownload()
    {
        return !IsBusy &&
            Uri.TryCreate(AppUpdateDownloadUri, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanOpenAppUpdatePlan()
    {
        return !IsBusy && File.Exists(AppUpdatePlanPath);
    }

    private bool CanOpenAppUpdateManifestCache()
    {
        return !IsBusy && File.Exists(AppUpdateManifestCachePath);
    }

    private bool CanOpenAppUpdateManifestExample()
    {
        return !IsBusy && File.Exists(AppUpdateManifestExamplePath);
    }

    private bool CanRestoreConfig()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ConfigRestorePath);
    }

    private bool CanUseLaragonImport()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(LaragonRootPath);
    }

    private bool CanOpenLaragonRoot()
    {
        return !IsBusy && TryResolveLaragonRootPath(out var rootPath, out _) && Directory.Exists(rootPath);
    }

    private bool CanToggleProjectPin(ProjectCard? project) => !IsBusy && project is not null;

    private bool CanRunServiceAction(ServiceStatusCard? service) => !IsBusy && service is not null;

    private bool CanRunServicePresetAction(ServicePresetCard? preset) => !IsBusy && preset?.CanRun == true;

    private bool CanRunCustomTool(CustomToolMenuItem? tool)
    {
        if (IsBusy || tool?.CanRun != true)
        {
            return false;
        }

        return NormalizeCustomToolAction(tool.Action) != "terminal" ||
            NormalizeTerminalCommandScope(tool.Scope) != "project" ||
            TryGetProjectTerminalSessionForCommand() is not null;
    }

    private bool CanStartLocalTunnel(LocalTunnelProfileCard? profile)
    {
        if (IsBusy || !ExternalAccessConsentAccepted || profile?.CanStart != true)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profile.ProjectName))
        {
            return FindProjectCard(profile.ProjectName) is not null;
        }

        return NormalizeTerminalCommandScope(profile.Scope) != "project" ||
            TryGetProjectTerminalSessionForCommand() is not null ||
            Projects.Count > 0;
    }

    private bool CanUseShareUrl(ShareUrlSessionCard? session)
    {
        return !IsBusy && session?.HasPublicUrl == true;
    }

    private bool CanStopShareUrlSession(ShareUrlSessionCard? session)
    {
        return !IsBusy && session?.CanStop == true;
    }

    private bool CanClearStoppedShareUrls()
    {
        return !IsBusy && ShareUrlSessions.Any(session => !session.IsRunning);
    }

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

    private static string ResolveDeveloperName()
    {
        var companyName = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

        return string.IsNullOrWhiteSpace(companyName) ? "CodeLevel8 Team" : companyName;
    }

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

    private async Task StartServicePresetAsync(ServicePresetCard? preset)
    {
        if (preset is null || !preset.CanRun)
        {
            return;
        }

        await ExecuteAsync(
            $"Start requested for service preset {preset.DisplayName}",
            () => _workbenchService.StartServicePresetAsync(preset.Key));
    }

    private async Task StopServicePresetAsync(ServicePresetCard? preset)
    {
        if (preset is null || !preset.CanRun)
        {
            return;
        }

        await ExecuteAsync(
            $"Stop requested for service preset {preset.DisplayName}",
            () => _workbenchService.StopServicePresetAsync(preset.Key));
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

    private async Task SaveActiveEnvironmentAsync()
    {
        var profileName = NewStackProfileName.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            ActiveEnvironmentSaveStatus = "Enter a stack profile name before saving the active environment.";
            return;
        }

        ActiveEnvironmentSaveStatus = $"Saving active environment as {profileName}.";

        var saved = await ExecuteAsync(
            $"Saving active environment as stack profile {profileName}",
            () => _workbenchService.SaveActiveEnvironmentAsync(profileName));

        if (saved)
        {
            ActiveEnvironmentSaveStatus = $"Saved {profileName} as a stack profile. Load it from the profile list to restore its services and packages.";
            NewStackProfileName = $"Saved Environment {DateTime.Now:yyyyMMdd HHmm}";
        }
        else
        {
            ActiveEnvironmentSaveStatus = $"Could not save {profileName}. Check the activity log and supervisor status, then try again.";
        }
    }

    private async Task ExportStackProfilesAsync()
    {
        if (!TryResolveProfileTransferPath(out var targetPath, out var validationMessage))
        {
            ProfileTransferStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        ProfileTransferStatus = $"Exporting stack profiles to {targetPath}.";

        var exported = await ExecuteAsync(
            $"Exporting stack profiles to {targetPath}",
            () => _workbenchService.ExportStackProfilesAsync(targetPath));

        ProfileTransferStatus = exported
            ? $"Exported stack profiles to {targetPath}."
            : $"Could not export stack profiles to {targetPath}. Check the activity log and supervisor status, then try again.";
    }

    private async Task ImportStackProfilesAsync()
    {
        if (!TryResolveProfileTransferPath(out var sourcePath, out var validationMessage))
        {
            ProfileTransferStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            ProfileTransferStatus = $"Import file was not found: {sourcePath}";
            RecordActivity("Warning", ProfileTransferStatus);
            return;
        }

        ProfileTransferStatus = $"Importing stack profiles from {sourcePath}.";

        var imported = await ExecuteAsync(
            $"Importing stack profiles from {sourcePath}",
            () => _workbenchService.ImportStackProfilesAsync(sourcePath));

        ProfileTransferStatus = imported
            ? $"Imported stack profiles from {sourcePath}. Existing profile keys were preserved; duplicates were added with unique keys."
            : $"Could not import stack profiles from {sourcePath}. Check the activity log and supervisor status, then try again.";
    }

    private async Task BackupConfigAsync()
    {
        try
        {
            IsBusy = true;
            var backup = await CreateConfigBackupArchiveAsync("locora-config");

            LatestConfigBackupPath = backup.Path;
            ConfigRestorePath = backup.Path;
            ConfigBackupStatus = $"Backed up {backup.FileCount} config file{(backup.FileCount == 1 ? string.Empty : "s")} to {backup.Path}.";
            RecordActivity("Info", ConfigBackupStatus);
        }
        catch (DirectoryNotFoundException exception)
        {
            _logger.LogWarning(exception, "Config backup failed.");
            ConfigBackupStatus = $"Configuration folder was not found: {ConfigRoot}";
            RecordActivity("Warning", ConfigBackupStatus);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Config backup failed.");
            ConfigBackupStatus = $"Config backup failed: {exception.Message}";
            RecordActivity("Error", ConfigBackupStatus);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreConfigAsync()
    {
        if (!TryResolveConfigRestorePath(out var restorePath, out var validationMessage))
        {
            ConfigRestoreStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        if (!File.Exists(restorePath))
        {
            ConfigRestoreStatus = $"Config backup archive was not found: {restorePath}";
            RecordActivity("Warning", ConfigRestoreStatus);
            return;
        }

        try
        {
            IsBusy = true;
            ConfigRestoreStatus = $"Restoring configuration backup from {restorePath}.";
            RecordActivity("Info", ConfigRestoreStatus);

            var preRestoreBackupPath = string.Empty;
            var preRestoreBackupFileCount = 0;
            if (Directory.Exists(ConfigRoot))
            {
                var preRestoreBackup = await CreateConfigBackupArchiveAsync("locora-config-before-restore");
                preRestoreBackupPath = preRestoreBackup.Path;
                preRestoreBackupFileCount = preRestoreBackup.FileCount;
                LatestConfigBackupPath = preRestoreBackup.Path;
                ConfigBackupStatus = $"Created pre-restore backup of {preRestoreBackup.FileCount} config file{(preRestoreBackup.FileCount == 1 ? string.Empty : "s")} at {preRestoreBackup.Path}.";
                RecordActivity("Info", ConfigBackupStatus);
            }
            else
            {
                Directory.CreateDirectory(ConfigRoot);
                ConfigBackupStatus = $"No pre-restore backup was created because the configuration folder did not exist: {ConfigRoot}";
                RecordActivity("Warning", ConfigBackupStatus);
            }

            var restoreTempRoot = Path.Combine(_environmentPaths.TempRoot, "config-restore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(restoreTempRoot);

            int extractedFileCount;
            int restoredFileCount;
            try
            {
                extractedFileCount = ExtractConfigBackupArchive(restorePath, restoreTempRoot);
                if (extractedFileCount == 0)
                {
                    ConfigRestoreStatus = $"Config backup archive did not contain restorable files: {restorePath}";
                    RecordActivity("Warning", ConfigRestoreStatus);
                    return;
                }

                restoredFileCount = ApplyExtractedConfigFiles(restoreTempRoot);
            }
            finally
            {
                TryDeleteRestoreTempRoot(restoreTempRoot);
            }

            var backupSuffix = string.IsNullOrWhiteSpace(preRestoreBackupPath)
                ? "No previous config folder was found before restore."
                : $"Pre-restore backup: {preRestoreBackupPath} ({preRestoreBackupFileCount} file{(preRestoreBackupFileCount == 1 ? string.Empty : "s")}).";
            ConfigRestoreStatus = $"Restored {restoredFileCount} config file{(restoredFileCount == 1 ? string.Empty : "s")} from {restorePath}. {backupSuffix}";
            RecordActivity("Info", ConfigRestoreStatus);

            try
            {
                RecordActivity("Info", "Refreshing environment snapshot after config restore.");
                var snapshot = await _workbenchService.GetSnapshotAsync();
                ApplySnapshot(snapshot);
            }
            catch (Exception refreshException)
            {
                _logger.LogWarning(refreshException, "Environment snapshot refresh failed after config restore.");
                RecordActivity("Warning", $"Config restored, but snapshot refresh failed: {refreshException.Message}");
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Config restore failed.");
            ConfigRestoreStatus = $"Config restore failed: {exception.Message}";
            RecordActivity("Error", ConfigRestoreStatus);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewLaragonImportAsync()
    {
        if (!TryResolveLaragonRootPath(out var rootPath, out var validationMessage))
        {
            LaragonImportStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        try
        {
            IsBusy = true;
            RecordActivity("Info", $"Previewing Laragon import from {rootPath}.");

            var currentDocument = NormalizeProjectSettingsDocument(await ReadProjectSettingsDocumentAsync());
            var plan = BuildLaragonImportPlan(rootPath, currentDocument);
            ApplyLaragonImportPlan(plan);
            LaragonImportSummary = CreateLaragonImportSummary(plan);
            LaragonImportStatus = plan.Projects.Count == 0
                ? $"No importable project folders were found in {plan.WwwRoot}."
                : $"Preview ready from {plan.WwwRoot}. Domain suffix: {plan.DomainSuffix}; source: {plan.DomainSource}.";
            RecordActivity("Info", $"Laragon import preview found {plan.Projects.Count} project(s).");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Laragon import preview failed.");
            ReplaceCollection(LaragonImportProjects, Array.Empty<LaragonImportProjectCard>());
            NotifyLaragonImportCollectionProperties();
            LaragonImportSummary = "Laragon import preview failed.";
            LaragonImportStatus = $"Laragon import preview failed: {exception.Message}";
            RecordActivity("Error", LaragonImportStatus);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportLaragonProjectsAsync()
    {
        if (!TryResolveLaragonRootPath(out var rootPath, out var validationMessage))
        {
            LaragonImportStatus = validationMessage;
            RecordActivity("Warning", validationMessage);
            return;
        }

        LaragonImportPlan plan;
        string? backupPath = null;
        int addedCount;
        int updatedCount;

        try
        {
            IsBusy = true;
            RecordActivity("Info", $"Importing Laragon projects from {rootPath}.");

            var currentDocument = NormalizeProjectSettingsDocument(await ReadProjectSettingsDocumentAsync());
            plan = BuildLaragonImportPlan(rootPath, currentDocument);
            if (plan.Projects.Count == 0)
            {
                ApplyLaragonImportPlan(plan);
                LaragonImportSummary = CreateLaragonImportSummary(plan);
                LaragonImportStatus = $"No importable project folders were found in {plan.WwwRoot}.";
                RecordActivity("Warning", LaragonImportStatus);
                return;
            }

            var mergedOverrides = MergeLaragonProjectOverrides(
                currentDocument.LocoraProjects.ProjectOverrides,
                plan.Projects,
                out addedCount,
                out updatedCount);
            var nextDocument = new ProjectSettingsDocument(
                currentDocument.LocoraProjects with
                {
                    DomainSuffix = plan.DomainSuffix,
                    DefaultScheme = plan.DefaultScheme,
                    ProjectOverrides = mergedOverrides
                });

            backupPath = CreateLaragonProjectSettingsBackup();
            Directory.CreateDirectory(Path.GetDirectoryName(_environmentPaths.ProjectsSettingsFile) ?? _environmentPaths.ConfigRoot);
            var content = JsonSerializer.Serialize(nextDocument, ProjectSettingsSerializerOptions);
            await File.WriteAllTextAsync(_environmentPaths.ProjectsSettingsFile, content);

            GeneratedDomainSuffix = plan.DomainSuffix;
            GeneratedDomainScheme = plan.DefaultScheme;
            ApplyLaragonImportPlan(plan);
            LaragonImportSummary = CreateLaragonImportSummary(plan);
            LaragonImportStatus = $"Imported {plan.Projects.Count} Laragon project override{(plan.Projects.Count == 1 ? string.Empty : "s")} into projects.json ({addedCount} added, {updatedCount} updated).";
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                LaragonImportStatus += $" Previous projects.json backup: {backupPath}";
            }

            RecordActivity("Info", LaragonImportStatus);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Laragon import failed.");
            LaragonImportStatus = $"Laragon import failed: {exception.Message}";
            RecordActivity("Error", LaragonImportStatus);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ExecuteAsync(
            "Regenerating hosts and vhosts after Laragon import",
            () => _workbenchService.RepairDomainsAsync());
    }

    private async Task RepairLocalSslAsync()
    {
        await ExecuteAsync(
            "Local SSL repair requested",
            () => _workbenchService.RepairLocalSslAsync());
    }

    private async Task ApplyHostsPreviewAsync()
    {
        if (!await ConfirmPrivilegedActionAsync(
                "Apply generated hosts entries?",
                $"Locora will write the reviewed hosts preview into {WindowsHostsFilePath}. Windows may prompt for elevation.",
                "Apply Hosts",
                "Hosts preview apply cancelled."))
        {
            return;
        }

        await ExecuteAsync(
            "Apply hosts preview requested",
            () => _workbenchService.ApplyHostsPreviewAsync());
    }

    private async Task RollbackHostsAsync()
    {
        if (!await ConfirmPrivilegedActionAsync(
                "Restore previous hosts backup?",
                $"Locora will restore the latest backup for {WindowsHostsFilePath}. Windows may prompt for elevation.",
                "Restore Hosts",
                "Hosts rollback cancelled."))
        {
            return;
        }

        await ExecuteAsync(
            "Hosts rollback requested",
            () => _workbenchService.RollbackHostsAsync());
    }

    private async Task RestartSupervisorElevatedAsync()
    {
        if (!await ConfirmPrivilegedActionAsync(
                "Restart supervisor with elevation?",
                "Locora will relaunch the supervisor with administrator rights. Managed services may briefly recycle while the elevated process takes over.",
                "Restart Elevated",
                "Elevated supervisor restart cancelled."))
        {
            return;
        }

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
        if (!await ConfirmPrivilegedActionAsync(
                "Trust the Locora certificate authority?",
                "Locora will add its local certificate authority to the CurrentUser trusted root store so generated HTTPS domains stop warning in this profile.",
                "Trust CA",
                "Local SSL trust change cancelled."))
        {
            return;
        }

        await ExecuteAsync(
            "Trust local SSL certificate authority requested",
            () => _workbenchService.TrustLocalSslCaAsync());
    }

    private async Task RollbackLocalSslTrustAsync()
    {
        if (!await ConfirmPrivilegedActionAsync(
                "Remove the Locora certificate authority from trust?",
                "Locora will remove its local certificate authority from the CurrentUser trusted root store. Locora HTTPS sites may warn again until you trust it later.",
                "Remove Trust",
                "Local SSL trust rollback cancelled."))
        {
            return;
        }

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

    private void OpenConfigBackupRoot()
    {
        Directory.CreateDirectory(ConfigBackupRootPath);
        ExecuteDesktopAction(
            "Config backups folder opened.",
            "Config backups folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(ConfigBackupRootPath));
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

    private void OpenProfilesRoot()
    {
        ExecuteDesktopAction(
            "Stack profiles folder opened.",
            "Stack profiles folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(ProfilesRootPath));
    }

    private void OpenLaragonRoot()
    {
        if (!TryResolveLaragonRootPath(out var rootPath, out var validationMessage) || !Directory.Exists(rootPath))
        {
            LaragonImportStatus = string.IsNullOrWhiteSpace(validationMessage)
                ? "Laragon root folder was not found."
                : validationMessage;
            RecordActivity("Warning", LaragonImportStatus);
            return;
        }

        ExecuteDesktopAction(
            "Laragon root folder opened.",
            "Laragon root folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(rootPath));
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

    private void OpenCustomToolsSettingsFile()
    {
        ExecuteDesktopAction(
            "Custom tools file opened.",
            "Custom tools file could not be opened.",
            () => _projectActionLauncher.OpenFile(CustomToolsSettingsFilePath));
    }

    private void OpenLocalTunnelsSettingsFile()
    {
        ExecuteDesktopAction(
            "Local tunnel profiles file opened.",
            "Local tunnel profiles file could not be opened.",
            () => _projectActionLauncher.OpenFile(LocalTunnelsSettingsFilePath));
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

    private void RefreshUserEnvironmentFiles()
    {
        ExecuteDesktopAction(
            "PATH and environment scripts refreshed.",
            "PATH and environment scripts could not be refreshed.",
            RefreshUserEnvironmentChangeFilesCore);
    }

    private void OpenUserEnvironmentApplyScript()
    {
        ExecuteDesktopAction(
            "Environment install script opened.",
            "Environment install script could not be opened.",
            () => _projectActionLauncher.OpenFile(UserEnvironmentApplyScriptPath));
    }

    private void OpenUserEnvironmentRemoveScript()
    {
        ExecuteDesktopAction(
            "Environment uninstall script opened.",
            "Environment uninstall script could not be opened.",
            () => _projectActionLauncher.OpenFile(UserEnvironmentRemoveScriptPath));
    }

    private void OpenUserEnvironmentManifest()
    {
        ExecuteDesktopAction(
            "Environment manifest opened.",
            "Environment manifest could not be opened.",
            () => _projectActionLauncher.OpenFile(UserEnvironmentManifestPath));
    }

    private void RefreshUserEnvironmentChangeFilesCore()
    {
        var files = _userEnvironmentChangeService.RefreshChangeFiles();
        UserEnvironmentApplyScriptPath = files.ApplyScriptPath;
        UserEnvironmentRemoveScriptPath = files.RemoveScriptPath;
        UserEnvironmentManifestPath = files.ManifestPath;
        UserEnvironmentStatus = $"Generated CurrentUser scripts for {files.ManagedPathEntries.Count} PATH entry and {files.ManagedVariables.Count} LOCORA variables. Each script writes a backup before changing user environment values.";
    }

    private async Task CheckForAppUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await CheckForAppUpdatesCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckForAppUpdatesCoreAsync()
    {
        RecordActivity("Info", "Checking for Locora app updates.");
        var status = await _appUpdateService.CheckForUpdatesAsync();
        ApplyAppUpdateStatus(status);

        var level = status.State.Equals("Check failed", StringComparison.OrdinalIgnoreCase)
            ? "Error"
            : status.IsUpdateAvailable
                ? "Warning"
                : "Info";
        RecordActivity(level, status.Summary);
    }

    private void OpenAppUpdateReleasePage()
    {
        ExecuteDesktopAction(
            "App update release page opened.",
            "App update release page could not be opened.",
            () => _projectActionLauncher.OpenUrl(AppUpdateReleasePageUri));
    }

    private void OpenAppUpdateDownload()
    {
        ExecuteDesktopAction(
            "App update download opened.",
            "App update download could not be opened.",
            () => _projectActionLauncher.OpenUrl(AppUpdateDownloadUri));
    }

    private void OpenAppUpdatePlan()
    {
        ExecuteDesktopAction(
            "App update plan opened.",
            "App update plan could not be opened.",
            () => _projectActionLauncher.OpenFile(AppUpdatePlanPath));
    }

    private void OpenAppUpdateManifestCache()
    {
        ExecuteDesktopAction(
            "Cached app update manifest opened.",
            "Cached app update manifest could not be opened.",
            () => _projectActionLauncher.OpenFile(AppUpdateManifestCachePath));
    }

    private void OpenAppUpdateManifestExample()
    {
        ExecuteDesktopAction(
            "Example app update manifest opened.",
            "Example app update manifest could not be opened.",
            () => _projectActionLauncher.OpenFile(AppUpdateManifestExamplePath));
    }

    private void ApplyAppUpdateStatus(AppUpdateStatus status)
    {
        AppUpdateState = status.State;
        AppUpdateSummary = status.Summary;
        AppUpdateDetails = status.Details;
        AppUpdateCurrentVersion = status.CurrentVersion;
        AppUpdateLatestVersion = string.IsNullOrWhiteSpace(status.LatestVersion) ? "(unknown)" : status.LatestVersion;
        AppUpdateCheckedAtLabel = status.CheckedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        AppUpdateReleasePageUri = status.ReleasePageUri;
        AppUpdateDownloadUri = status.DownloadUri;
        AppUpdateSha256 = string.IsNullOrWhiteSpace(status.Sha256) ? "(not provided)" : status.Sha256;
        NotifyAppUpdateCommandProperties();
    }

    private void NotifyAppUpdateCommandProperties()
    {
        CheckForAppUpdatesCommand.NotifyCanExecuteChanged();
        OpenAppUpdateReleasePageCommand.NotifyCanExecuteChanged();
        OpenAppUpdateDownloadCommand.NotifyCanExecuteChanged();
        OpenAppUpdatePlanCommand.NotifyCanExecuteChanged();
        OpenAppUpdateManifestCacheCommand.NotifyCanExecuteChanged();
        OpenAppUpdateManifestExampleCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPortableDistributionFiles()
    {
        ExecuteDesktopAction(
            "Portable distribution files refreshed.",
            "Portable distribution files could not be refreshed.",
            RefreshPortableDistributionFilesCore);
    }

    private void OpenPortableDistributionRoot()
    {
        ExecuteDesktopAction(
            "Portable distribution folder opened.",
            "Portable distribution folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(PortableDistributionRootPath));
    }

    private void OpenPortableDistributionArtifactsRoot()
    {
        ExecuteDesktopAction(
            "Portable distribution artifacts folder opened.",
            "Portable distribution artifacts folder could not be opened.",
            () => _projectActionLauncher.OpenFolder(PortableDistributionArtifactsRootPath));
    }

    private void OpenPortableDistributionScript()
    {
        ExecuteDesktopAction(
            "Portable distribution script opened.",
            "Portable distribution script could not be opened.",
            () => _projectActionLauncher.OpenFile(PortableDistributionScriptPath));
    }

    private void OpenPortableDistributionPlan()
    {
        ExecuteDesktopAction(
            "Portable distribution plan opened.",
            "Portable distribution plan could not be opened.",
            () => _projectActionLauncher.OpenFile(PortableDistributionPlanPath));
    }

    private void OpenPortableDistributionReadme()
    {
        ExecuteDesktopAction(
            "Portable distribution README opened.",
            "Portable distribution README could not be opened.",
            () => _projectActionLauncher.OpenFile(PortableDistributionReadmePath));
    }

    private void OpenPortableDistributionManifestTemplate()
    {
        ExecuteDesktopAction(
            "Portable distribution release manifest template opened.",
            "Portable distribution release manifest template could not be opened.",
            () => _projectActionLauncher.OpenFile(PortableDistributionManifestTemplatePath));
    }

    private void RefreshPortableDistributionFilesCore()
    {
        var files = _portableDistributionService.RefreshDistributionFiles();
        PortableDistributionScriptPath = files.ScriptPath;
        PortableDistributionPlanPath = files.PlanPath;
        PortableDistributionReadmePath = files.ReadmePath;
        PortableDistributionManifestTemplatePath = files.ManifestTemplatePath;
        PortableDistributionStatus = $"Generated portable distribution automation for {files.Configuration}/{files.RuntimeIdentifier}. The script stages a portable ZIP, checksums, and release manifest under {files.ArtifactsRootPath}.";
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

    private void RunCustomTool(CustomToolMenuItem? tool)
    {
        if (tool is null)
        {
            return;
        }

        try
        {
            var action = NormalizeCustomToolAction(tool.Action);
            if (action == "terminal")
            {
                RunCustomTerminalTool(tool);
                return;
            }

            var target = ResolveCustomToolTarget(tool);
            switch (action)
            {
                case "url":
                    _projectActionLauncher.OpenUrl(target);
                    break;
                case "folder":
                    _projectActionLauncher.OpenFolder(ResolveCustomToolPath(target));
                    break;
                case "file":
                    _projectActionLauncher.OpenFile(ResolveCustomToolPath(target));
                    break;
                case "editor":
                    _projectActionLauncher.OpenEditor(ResolveCustomToolPath(target));
                    break;
                default:
                    RecordActivity("Warning", $"Unsupported custom tool action: {tool.Action}");
                    return;
            }

            RecordActivity("Info", $"Ran custom tool: {tool.DisplayName}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Custom tool failed for {Tool}.", tool.DisplayName);
            RecordActivity("Error", $"Custom tool failed for {tool.DisplayName}: {exception.Message}");
        }
    }

    private void RunCustomTerminalTool(CustomToolMenuItem tool)
    {
        var command = new TerminalQuickCommand(
            tool.Key,
            tool.DisplayName,
            string.Empty,
            tool.Description,
            tool.Target,
            NormalizeTerminalCommandScope(tool.Scope),
            tool.Tags);
        var session = ResolveTerminalSessionForCommand(command);
        if (session is null)
        {
            RecordActivity("Warning", "Open a project terminal tab before running this custom tool.");
            return;
        }

        var commandText = ResolveTerminalCommandText(command, session);
        if (string.IsNullOrWhiteSpace(commandText))
        {
            RecordActivity("Warning", $"Custom tool '{tool.DisplayName}' resolved to an empty command.");
            return;
        }

        SelectedTerminalSession = session;
        NavigationRequested?.Invoke(this, "terminal");
        _terminalSessionService.SendInput(session, commandText);
        RecordActivity("Info", $"Ran custom tool: {tool.DisplayName}");
    }

    private void StartLocalTunnel(LocalTunnelProfileCard? profile)
    {
        if (profile is null)
        {
            return;
        }

        if (!ExternalAccessConsentAccepted)
        {
            RecordActivity("Warning", "Confirm external access consent before starting a local tunnel.");
            return;
        }

        try
        {
            var project = ResolveLocalTunnelProject(profile);
            var session = ResolveLocalTunnelSession(profile, project);
            if (session is null)
            {
                RecordActivity("Warning", "Open or discover a project before starting this local tunnel.");
                return;
            }

            var commandText = ResolveLocalTunnelCommandText(profile, session, project);
            if (string.IsNullOrWhiteSpace(commandText))
            {
                RecordActivity("Warning", $"Local tunnel '{profile.DisplayName}' resolved to an empty command.");
                return;
            }

            RegisterShareUrlSession(profile, session, project, commandText);
            SelectedTerminalSession = session;
            NavigationRequested?.Invoke(this, "terminal");
            _terminalSessionService.SendInput(session, commandText);
            RecordActivity("Warning", $"Started local tunnel profile: {profile.DisplayName}. Review the terminal output for the public URL.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Local tunnel profile failed for {Profile}.", profile.DisplayName);
            RecordActivity("Error", $"Local tunnel failed for {profile.DisplayName}: {exception.Message}");
        }
    }

    private void CopyShareUrl(ShareUrlSessionCard? session)
    {
        if (session?.HasPublicUrl != true)
        {
            return;
        }

        ExecuteDesktopAction(
            $"Copied share URL: {session.ProfileName}",
            $"Share URL could not be copied: {session.ProfileName}",
            () => _clipboardService.CopyText(session.PublicUrl));
    }

    private void OpenShareUrl(ShareUrlSessionCard? session)
    {
        if (session?.HasPublicUrl != true)
        {
            return;
        }

        ExecuteDesktopAction(
            $"Opened share URL: {session.ProfileName}",
            $"Share URL could not be opened: {session.ProfileName}",
            () => _projectActionLauncher.OpenUrl(session.PublicUrl));
    }

    private void StopShareUrlSession(ShareUrlSessionCard? shareSession)
    {
        if (shareSession is null)
        {
            return;
        }

        var terminalSession = TerminalSessions.FirstOrDefault(session =>
            session.Id.Equals(shareSession.SessionId, StringComparison.OrdinalIgnoreCase));
        if (terminalSession is not null)
        {
            _terminalSessionService.StopSession(terminalSession);
            shareSession.MarkStopped("Stop requested from share URL lifecycle controls.");
            RecordActivity("Info", $"Stopped share URL session: {shareSession.ProfileName}");
        }
        else
        {
            shareSession.MarkStopped("The terminal session for this share URL is no longer available.");
            RecordActivity("Warning", $"Share URL session already stopped: {shareSession.ProfileName}");
        }

        NotifyShareUrlSessionStateChanged();
    }

    private void ClearStoppedShareUrls()
    {
        var stoppedSessions = ShareUrlSessions.Where(session => !session.IsRunning).ToList();
        foreach (var stoppedSession in stoppedSessions)
        {
            var terminalSession = TerminalSessions.FirstOrDefault(session =>
                session.Id.Equals(stoppedSession.SessionId, StringComparison.OrdinalIgnoreCase));
            if (terminalSession is not null)
            {
                RemoveShareSessionMonitoring(terminalSession);
            }

            ShareUrlSessions.Remove(stoppedSession);
            _shareSessionsByTerminalId.Remove(stoppedSession.SessionId);
            _shareMonitoredTerminalIds.Remove(stoppedSession.SessionId);
        }

        NotifyShareUrlSessionCollectionProperties();
        RecordActivity("Info", $"Cleared {stoppedSessions.Count} stopped share URL session{(stoppedSessions.Count == 1 ? string.Empty : "s")}.");
    }

    private async Task ReloadCustomToolsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadCustomToolsAsync();
            RecordActivity("Info", "Custom tools menu reloaded.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadLocalTunnelsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await LoadLocalTunnelsAsync();
            RecordActivity("Info", "Local tunnel profiles reloaded.");
        }
        finally
        {
            IsBusy = false;
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

    private TerminalSession? ResolveLocalTunnelSession(LocalTunnelProfileCard profile, ProjectCard? project)
    {
        var scope = NormalizeTerminalCommandScope(profile.Scope);
        if (project is not null)
        {
            return StartTerminalSession(
                CreateLocalTunnelTerminalTitle(profile, project),
                project.TerminalLaunchProfile,
                $"Opened local tunnel terminal: {profile.DisplayName}");
        }

        if (scope == "root")
        {
            return StartTerminalSession(
                CreateLocalTunnelTerminalTitle(profile, null),
                CreateRootTerminalLaunchProfile(),
                $"Opened local tunnel terminal: {profile.DisplayName}");
        }

        if (scope == "project")
        {
            var projectSession = TryGetProjectTerminalSessionForCommand();
            if (projectSession is not null)
            {
                return StartTerminalSession(
                    CreateLocalTunnelTerminalTitle(profile, null),
                    projectSession.LaunchProfile,
                    $"Opened local tunnel terminal: {profile.DisplayName}");
            }

            var fallbackProject = Projects.FirstOrDefault(project => project.IsPinned) ?? Projects.FirstOrDefault();
            return fallbackProject is null
                ? null
                : StartTerminalSession(
                    CreateLocalTunnelTerminalTitle(profile, fallbackProject),
                    fallbackProject.TerminalLaunchProfile,
                    $"Opened local tunnel terminal: {profile.DisplayName}");
        }

        if (SelectedTerminalSession is not null)
        {
            return StartTerminalSession(
                CreateLocalTunnelTerminalTitle(profile, null),
                SelectedTerminalSession.LaunchProfile,
                $"Opened local tunnel terminal: {profile.DisplayName}");
        }

        return StartTerminalSession(
            CreateLocalTunnelTerminalTitle(profile, null),
            CreateRootTerminalLaunchProfile(),
            $"Opened local tunnel terminal: {profile.DisplayName}");
    }

    private TerminalSession? ResolveOrStartProjectTerminalSession(ProjectCard project)
    {
        if (SelectedTerminalSession is not null && IsTerminalSessionForProject(SelectedTerminalSession, project))
        {
            return EnsureRunningTerminalSession(SelectedTerminalSession);
        }

        var existingProjectSession = TerminalSessions.LastOrDefault(session => IsTerminalSessionForProject(session, project));
        return existingProjectSession is not null
            ? EnsureRunningTerminalSession(existingProjectSession)
            : StartTerminalSession(project.Name, project.TerminalLaunchProfile, $"Opened built-in terminal: {project.Name}");
    }

    private static bool IsTerminalSessionForProject(TerminalSession session, ProjectCard project)
    {
        return session.IsProjectScoped &&
            session.LaunchProfile.EnvironmentVariables.TryGetValue("LOCORA_PROJECT_ROOT", out var projectRoot) &&
            string.Equals(projectRoot, project.Folder, StringComparison.OrdinalIgnoreCase);
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

    private void RegisterShareUrlSession(
        LocalTunnelProfileCard profile,
        TerminalSession terminalSession,
        ProjectCard? project,
        string commandText)
    {
        var localUrl = ResolveLocalTunnelLocalUrl(profile, terminalSession, project);
        var key = $"{terminalSession.Id}-{profile.Key}";
        var shareSession = new ShareUrlSessionCard(
            key,
            profile.Key,
            profile.DisplayName,
            profile.Provider,
            localUrl,
            commandText,
            terminalSession.Id,
            terminalSession.Title,
            project?.Name ?? GetTerminalLaunchProfileValue(terminalSession.LaunchProfile, "LOCORA_PROJECT_NAME", string.Empty),
            DateTimeOffset.Now,
            CopyShareUrlCommand,
            OpenShareUrlCommand,
            StopShareUrlSessionCommand);

        _shareSessionsByTerminalId[terminalSession.Id] = shareSession;
        ShareUrlSessions.Insert(0, shareSession);
        EnsureShareSessionMonitoring(terminalSession);
        ExtractShareUrlFromTerminalSession(terminalSession);
        NotifyShareUrlSessionCollectionProperties();
    }

    private void EnsureShareSessionMonitoring(TerminalSession terminalSession)
    {
        if (_shareMonitoredTerminalIds.Add(terminalSession.Id))
        {
            terminalSession.PropertyChanged += OnShareTerminalSessionPropertyChanged;
        }
    }

    private void RemoveShareSessionMonitoring(TerminalSession terminalSession)
    {
        if (_shareMonitoredTerminalIds.Remove(terminalSession.Id))
        {
            terminalSession.PropertyChanged -= OnShareTerminalSessionPropertyChanged;
        }
    }

    private void ExtractShareUrlFromTerminalSession(TerminalSession terminalSession)
    {
        if (!_shareSessionsByTerminalId.TryGetValue(terminalSession.Id, out var shareSession))
        {
            return;
        }

        var matches = PublicShareUrlRegex.Matches(terminalSession.Output);
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            var candidate = CleanShareUrl(matches[index].Value);
            if (!IsPublicShareUrl(candidate))
            {
                continue;
            }

            shareSession.MarkPublicUrl(candidate);
            NotifyShareUrlSessionStateChanged();
            return;
        }

        if (!terminalSession.IsRunning && !shareSession.HasPublicUrl)
        {
            shareSession.MarkStopped("The tunnel terminal stopped before a public URL was detected.");
            NotifyShareUrlSessionStateChanged();
        }
    }

    private async Task<bool> ExecuteAsync(string activityMessage, Func<Task<EnvironmentSnapshot>> action)
    {
        if (IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            RecordActivity("Info", activityMessage);

            var snapshot = await action();
            ApplySnapshot(snapshot);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Workbench action failed.");
            RecordActivity("Error", exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ConfirmPrivilegedActionAsync(
        string title,
        string message,
        string primaryButtonText,
        string cancellationMessage)
    {
        try
        {
            if (await _userConfirmationService.ConfirmAsync(title, message, primaryButtonText))
            {
                return true;
            }

            RecordActivity("Warning", cancellationMessage);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not show privileged action confirmation for {Title}.", title);
            RecordActivity("Error", $"Privileged action confirmation failed: {exception.Message}");
        }

        return false;
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
            ServicePresets,
            snapshot.ServicePresets.Select(preset => new ServicePresetCard(
                preset.Key,
                preset.DisplayName,
                preset.Description,
                preset.State,
                preset.ServicesLabel,
                CreateTagsLabel(preset.Tags),
                preset.IsValid,
                preset.Summary,
                preset.Details,
                StartServicePresetCommand,
                StopServicePresetCommand)));

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
                validation.CheckedAt.LocalDateTime.ToString("g"),
                validation.Key)));

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
        RefreshNetworkGuidance();

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
        OnPropertyChanged(nameof(RuntimeBackupStatusLabel));
        OnPropertyChanged(nameof(PortableRelocationValidationStatusLabel));
        OnPropertyChanged(nameof(PortableRelocationValidationDetails));
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
        StartLocalTunnelCommand.NotifyCanExecuteChanged();
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
                    project.OverrideSummary,
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
            ["LOCORA_PACKAGE_CACHE"] = _environmentPaths.PackageCacheRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_CUSTOM_TOOLS_FILE"] = _environmentPaths.CustomToolsSettingsFile,
            ["LOCORA_LOCAL_TUNNELS_FILE"] = _environmentPaths.LocalTunnelsSettingsFile,
            ["LOCORA_PROFILE"] = _lastSnapshot?.ActiveProfile ?? "Bootstrap",
            ["LOCORA_PROJECT_NAME"] = project.Name,
            ["LOCORA_PROJECT_ROOT"] = project.Path,
            ["LOCORA_PROJECT_URL"] = project.Url,
            ["LOCORA_PROJECT_RUNTIME"] = project.Runtime,
            ["LOCORA_PROJECT_HTTPS"] = project.UsesHttps ? "1" : "0",
            ["LOCORA_PROJECT_OVERRIDES"] = project.OverrideSummary,
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
            ["LOCORA_PACKAGE_CACHE"] = _environmentPaths.PackageCacheRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_CUSTOM_TOOLS_FILE"] = _environmentPaths.CustomToolsSettingsFile,
            ["LOCORA_LOCAL_TUNNELS_FILE"] = _environmentPaths.LocalTunnelsSettingsFile,
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
            ["LOCORA_PACKAGE_CACHE"] = _environmentPaths.PackageCacheRoot,
            ["LOCORA_TERMINAL_COMMANDS_FILE"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["LOCORA_CUSTOM_TOOLS_FILE"] = _environmentPaths.CustomToolsSettingsFile,
            ["LOCORA_LOCAL_TUNNELS_FILE"] = _environmentPaths.LocalTunnelsSettingsFile,
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
        if (args.OldItems is not null)
        {
            foreach (var oldItem in args.OldItems.OfType<TerminalSession>())
            {
                MarkShareSessionStopped(oldItem, "The terminal session for this share URL was closed.");
                RemoveShareSessionMonitoring(oldItem);
            }
        }

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
        NotifyShareUrlSessionStateChanged();
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

    private void OnShareTerminalSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not TerminalSession terminalSession)
        {
            return;
        }

        if (args.PropertyName is nameof(TerminalSession.Output))
        {
            ExtractShareUrlFromTerminalSession(terminalSession);
        }

        if (args.PropertyName is nameof(TerminalSession.IsRunning) && !terminalSession.IsRunning)
        {
            MarkShareSessionStopped(terminalSession, "The tunnel terminal process stopped.");
        }
    }

    private void MarkShareSessionStopped(TerminalSession terminalSession, string details)
    {
        if (!_shareSessionsByTerminalId.TryGetValue(terminalSession.Id, out var shareSession) || !shareSession.IsRunning)
        {
            return;
        }

        shareSession.MarkStopped(details);
        NotifyShareUrlSessionStateChanged();
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

    private void NotifyCustomToolsCollectionProperties()
    {
        OnPropertyChanged(nameof(HasCustomTools));
        OnPropertyChanged(nameof(ShowCustomToolsEmptyState));
        RunCustomToolCommand.NotifyCanExecuteChanged();
    }

    private void NotifyLocalTunnelsCollectionProperties()
    {
        OnPropertyChanged(nameof(HasLocalTunnels));
        OnPropertyChanged(nameof(ShowLocalTunnelsEmptyState));
        StartLocalTunnelCommand.NotifyCanExecuteChanged();
    }

    private void NotifyShareUrlSessionCollectionProperties()
    {
        OnPropertyChanged(nameof(HasShareUrlSessions));
        OnPropertyChanged(nameof(ShowShareUrlSessionsEmptyState));
        NotifyShareUrlSessionStateChanged();
    }

    private void NotifyShareUrlSessionStateChanged()
    {
        OnPropertyChanged(nameof(ShareUrlLifecycleSummary));
        CopyShareUrlCommand.NotifyCanExecuteChanged();
        OpenShareUrlCommand.NotifyCanExecuteChanged();
        StopShareUrlSessionCommand.NotifyCanExecuteChanged();
        ClearStoppedShareUrlsCommand.NotifyCanExecuteChanged();
    }

    private void NotifyNetworkGuidanceCollectionProperties()
    {
        OnPropertyChanged(nameof(HasNetworkGuidance));
        OnPropertyChanged(nameof(ShowNetworkGuidanceEmptyState));
        OnPropertyChanged(nameof(NetworkGuidanceSummary));
    }

    private void RefreshNetworkGuidance()
    {
        ReplaceCollection(NetworkGuidance, CreateNetworkGuidanceCards());
        NotifyNetworkGuidanceCollectionProperties();
    }

    private IEnumerable<NetworkGuidanceCard> CreateNetworkGuidanceCards()
    {
        var services = (_lastSnapshot?.Services ?? Array.Empty<ServiceDescriptor>())
            .Where(service => service.Port is > 0)
            .OrderBy(service => service.Port)
            .ThenBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var enabledTunnels = LocalTunnels
            .Where(profile => profile.IsEnabled)
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        yield return new NetworkGuidanceCard(
            "local-only-default",
            "Local-only default",
            "Baseline",
            CreatePortListLabel(services.Select(service => service.Port!.Value)),
            "Locora project domains and managed services are intended for local development by default.",
            "The generated hosts entries resolve to this Windows machine. Keep services bound to localhost unless another device on your private network needs temporary access.",
            "Use normal project URLs for local browser testing and avoid broad inbound firewall rules for everyday development.",
            "Info");

        yield return new NetworkGuidanceCard(
            "windows-firewall-inbound",
            "Windows Firewall inbound rules",
            "LAN testing",
            CreatePortListLabel(services.Select(service => service.Port!.Value)),
            CreateServicePortSummary(services),
            "Inbound Windows Firewall rules are only needed when another device connects directly to a Locora service over the LAN.",
            "Allow only the required TCP ports on the Private network profile, then remove or disable the rule after testing.",
            "Warning");

        yield return new NetworkGuidanceCard(
            "local-tunnel-outbound",
            "Local tunnel network path",
            "Public URL",
            CreateTunnelTargetLabel(enabledTunnels),
            CreateTunnelProfileSummary(enabledTunnels),
            "Tunnel CLIs usually create outbound connections to their provider, so provider-generated public URLs normally do not require Windows Firewall inbound rules.",
            "Allow outbound DNS and HTTPS or WebSocket traffic for the tunnel CLI, and stop the tunnel terminal when sharing is finished.",
            "Warning");

        var dataServices = services
            .Where(IsDataService)
            .ToList();
        if (dataServices.Count > 0)
        {
            yield return new NetworkGuidanceCard(
                "data-service-exposure",
                "Database and cache exposure",
                "Data services",
                CreatePortListLabel(dataServices.Select(service => service.Port!.Value)),
                $"{dataServices.Count} database, cache, SMTP, or data-service port{(dataServices.Count == 1 ? string.Empty : "s")} configured.",
                "Database, cache, and test-mail services can expose credentials, test data, or captured mail if shared directly.",
                "Share only the HTTP or HTTPS app port unless you explicitly need data-service access on a trusted private network.",
                "Warning");
        }

        if (_lastSnapshot?.SslStatus.HttpsProjectCount > 0)
        {
            yield return new NetworkGuidanceCard(
                "https-trust-boundary",
                "HTTPS trust boundary",
                "Certificates",
                $"{_lastSnapshot.SslStatus.HttpsProjectCount} HTTPS project{(_lastSnapshot.SslStatus.HttpsProjectCount == 1 ? string.Empty : "s")}",
                "Locora's local CA trust applies to this Windows user and machine.",
                "Other LAN devices and public tunnel visitors will not automatically trust local project certificates.",
                "Use provider-managed tunnel TLS for public URLs, or install the Locora CA only on trusted test devices.",
                "Info");
        }
    }

    private void NotifySelectedTerminalCommandProperties()
    {
        OnPropertyChanged(nameof(HasSelectedTerminalCommand));
        OnPropertyChanged(nameof(SelectedTerminalCommandHint));
        OnPropertyChanged(nameof(SelectedTerminalCommandResolvedText));
        RunTerminalCommandCommand.NotifyCanExecuteChanged();
        RunCustomToolCommand.NotifyCanExecuteChanged();
        StartLocalTunnelCommand.NotifyCanExecuteChanged();
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
        StartLocalTunnelCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(HasServicePresets));
        OnPropertyChanged(nameof(ShowServicePresetsEmptyState));
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

    private async Task LoadCustomToolsAsync()
    {
        try
        {
            var document = NormalizeCustomToolsDocument(await ReadCustomToolsDocumentAsync());
            ReplaceCollection(CustomTools, CreateCustomToolMenuItems(document));
            CustomToolsStatus = CustomTools.Count == 0
                ? "No custom tools are configured."
                : $"{CustomTools.Count} custom tool{(CustomTools.Count == 1 ? string.Empty : "s")} loaded from custom-tools.json.";
            NotifyCustomToolsCollectionProperties();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load custom tool settings.");
            ReplaceCollection(CustomTools, Array.Empty<CustomToolMenuItem>());
            CustomToolsStatus = $"Custom tool settings could not be loaded: {exception.Message}";
            NotifyCustomToolsCollectionProperties();
            RecordActivity("Warning", CustomToolsStatus);
        }
    }

    private async Task LoadLocalTunnelsAsync()
    {
        try
        {
            var document = NormalizeLocalTunnelsDocument(await ReadLocalTunnelsDocumentAsync());
            ReplaceCollection(LocalTunnels, CreateLocalTunnelProfileCards(document));
            LocalTunnelsStatus = LocalTunnels.Count == 0
                ? "No local tunnel profiles are configured."
                : $"{LocalTunnels.Count} local tunnel profile{(LocalTunnels.Count == 1 ? string.Empty : "s")} loaded from local-tunnels.json.";
            NotifyLocalTunnelsCollectionProperties();
            RefreshNetworkGuidance();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load local tunnel profiles.");
            ReplaceCollection(LocalTunnels, Array.Empty<LocalTunnelProfileCard>());
            LocalTunnelsStatus = $"Local tunnel profiles could not be loaded: {exception.Message}";
            NotifyLocalTunnelsCollectionProperties();
            RefreshNetworkGuidance();
            RecordActivity("Warning", LocalTunnelsStatus);
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

    private async Task<CustomToolsDocument?> ReadCustomToolsDocumentAsync()
    {
        var settingsPath = _environmentPaths.CustomToolsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<CustomToolsDocument>(stream, ProjectSettingsSerializerOptions);
    }

    private async Task<LocalTunnelsDocument?> ReadLocalTunnelsDocumentAsync()
    {
        var settingsPath = _environmentPaths.LocalTunnelsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<LocalTunnelsDocument>(stream, ProjectSettingsSerializerOptions);
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
                IgnoredDirectoryNames = NormalizeConfiguredNames(settings.IgnoredDirectoryNames, DefaultIgnoredDirectoryNames),
                ProjectOverrides = settings.ProjectOverrides ?? Array.Empty<ProjectOverrideSettings>()
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

    private static CustomToolsDocument NormalizeCustomToolsDocument(CustomToolsDocument? document)
    {
        if (document?.LocoraTools?.Tools is { Count: > 0 })
        {
            return document;
        }

        return CreateDefaultCustomToolsDocument();
    }

    private static LocalTunnelsDocument NormalizeLocalTunnelsDocument(LocalTunnelsDocument? document)
    {
        if (document?.LocoraTunnels?.Profiles is { Count: > 0 })
        {
            return document;
        }

        return CreateDefaultLocalTunnelsDocument();
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

    private static CustomToolsDocument CreateDefaultCustomToolsDocument()
    {
        return new CustomToolsDocument(
            new CustomToolsSection(
                new[]
                {
                    new CustomToolSettings("open-localhost", "Open localhost", "Open the default local HTTP endpoint in the browser.", "url", "http://localhost/", "root", new[] { "browser", "web" }, true),
                    new CustomToolSettings("open-config-folder", "Open config folder", "Open Locora's portable configuration folder.", "folder", "{configRoot}", "root", new[] { "config", "folder" }, true),
                    new CustomToolSettings("composer-diagnose", "Composer diagnose", "Run Composer diagnostics in the selected project terminal tab.", "terminal", "composer diagnose", "project", new[] { "composer", "php", "diagnostics" }, true)
                }));
    }

    private static LocalTunnelsDocument CreateDefaultLocalTunnelsDocument()
    {
        return new LocalTunnelsDocument(
            new LocalTunnelsSection(
                new[]
                {
                    new LocalTunnelSettings("cloudflared-selected-project", "Cloudflared quick tunnel", "cloudflared", "Expose the selected project URL through a temporary Cloudflare Tunnel.", "cloudflared tunnel --url {localUrl}", "project", string.Empty, "{projectUrl}", 80, new[] { "share", "cloudflared", "temporary" }, true),
                    new LocalTunnelSettings("ngrok-http-80", "Ngrok HTTP 80", "ngrok", "Expose the default local web server port with ngrok.", "ngrok http {port}", "root", string.Empty, "http://127.0.0.1:{port}", 80, new[] { "share", "ngrok", "http" }, true),
                    new LocalTunnelSettings("dev-tunnel-http-80", "Dev Tunnel HTTP 80", "devtunnel", "Expose the default local web server port with Microsoft dev tunnels.", "devtunnel host -p {port} --allow-anonymous", "root", string.Empty, "http://127.0.0.1:{port}", 80, new[] { "share", "devtunnel", "http" }, true)
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

    private IEnumerable<CustomToolMenuItem> CreateCustomToolMenuItems(CustomToolsDocument document)
    {
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in document.LocoraTools.Tools)
        {
            var target = tool.Target?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(tool.DisplayName)
                ? target
                : tool.DisplayName.Trim();
            var key = CreateTerminalCommandKey(tool.Key, displayName, string.Empty);
            if (!uniqueKeys.Add(key))
            {
                continue;
            }

            yield return new CustomToolMenuItem(
                key,
                displayName,
                tool.Description?.Trim() ?? string.Empty,
                NormalizeCustomToolAction(tool.Action),
                target,
                NormalizeTerminalCommandScope(tool.Scope),
                NormalizeConfiguredNames(tool.Tags, Array.Empty<string>()),
                tool.IsEnabled ?? true,
                RunCustomToolCommand);
        }
    }

    private IEnumerable<LocalTunnelProfileCard> CreateLocalTunnelProfileCards(LocalTunnelsDocument document)
    {
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in document.LocoraTunnels.Profiles)
        {
            var commandText = profile.CommandText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commandText))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? commandText
                : profile.DisplayName.Trim();
            var key = CreateTerminalCommandKey(profile.Key, displayName, string.Empty);
            if (!uniqueKeys.Add(key))
            {
                continue;
            }

            yield return new LocalTunnelProfileCard(
                key,
                displayName,
                profile.Provider?.Trim() ?? string.Empty,
                profile.Description?.Trim() ?? string.Empty,
                commandText,
                NormalizeTerminalCommandScope(profile.Scope),
                profile.ProjectName?.Trim() ?? string.Empty,
                profile.LocalUrl?.Trim() ?? string.Empty,
                profile.Port,
                NormalizeConfiguredNames(profile.Tags, Array.Empty<string>()),
                profile.IsEnabled ?? true,
                StartLocalTunnelCommand);
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

    private string ResolveLocalTunnelCommandText(LocalTunnelProfileCard profile, TerminalSession session, ProjectCard? project)
    {
        var launchProfile = session.LaunchProfile;
        var localUrl = ResolveLocalTunnelLocalUrl(profile, session, project);
        var port = profile.Port?.ToString() ?? ExtractPortFromUrl(localUrl) ?? "80";
        var extraReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{localUrl}"] = localUrl,
            ["{port}"] = port,
            ["{tunnelKey}"] = profile.Key,
            ["{tunnelName}"] = profile.DisplayName,
            ["{tunnelProvider}"] = profile.Provider
        };

        return ResolveLocalTunnelTemplate(
            profile.CommandText,
            launchProfile,
            session,
            profile,
            project,
            extraReplacements).Trim();
    }

    private string ResolveLocalTunnelLocalUrl(LocalTunnelProfileCard profile, TerminalSession session, ProjectCard? project)
    {
        var resolvedLocalUrl = ResolveLocalTunnelTemplate(
            string.IsNullOrWhiteSpace(profile.LocalUrl) ? "{projectUrl}" : profile.LocalUrl,
            session.LaunchProfile,
            session,
            profile,
            project,
            extraReplacements: null);
        var port = profile.Port?.ToString() ?? ExtractPortFromUrl(resolvedLocalUrl) ?? "80";

        return string.IsNullOrWhiteSpace(resolvedLocalUrl)
            ? $"http://127.0.0.1:{port}"
            : resolvedLocalUrl;
    }

    private string ResolveLocalTunnelTemplate(
        string template,
        ProjectTerminalLaunchProfile launchProfile,
        TerminalSession? session,
        LocalTunnelProfileCard profile,
        ProjectCard? project,
        IReadOnlyDictionary<string, string>? extraReplacements)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var environmentVariables = launchProfile.EnvironmentVariables;
        var workingDirectory = session?.WorkingDirectory ?? launchProfile.WorkingDirectory;
        var projectRoot = project?.Folder ?? GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_ROOT", workingDirectory);
        var projectName = project?.Name ?? GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_NAME", Path.GetFileName(projectRoot));
        var appUrl = GetTerminalLaunchProfileValue(launchProfile, "APP_URL", string.Empty);
        var projectUrl = project?.Url ?? GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROJECT_URL", appUrl);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{aliasesRoot}"] = _environmentPaths.AliasesRoot,
            ["{appUrl}"] = appUrl,
            ["{binRoot}"] = _environmentPaths.BinRoot,
            ["{configRoot}"] = _environmentPaths.ConfigRoot,
            ["{cwd}"] = workingDirectory,
            ["{customToolsFile}"] = _environmentPaths.CustomToolsSettingsFile,
            ["{dataRoot}"] = _environmentPaths.DataRoot,
            ["{localTunnelsFile}"] = _environmentPaths.LocalTunnelsSettingsFile,
            ["{locoraProfile}"] = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROFILE", _lastSnapshot?.ActiveProfile ?? "Bootstrap"),
            ["{locoraRoot}"] = _environmentPaths.AppRoot,
            ["{logsRoot}"] = _environmentPaths.LogsRoot,
            ["{projectName}"] = projectName,
            ["{projectRoot}"] = projectRoot,
            ["{projectUrl}"] = projectUrl,
            ["{sessionTitle}"] = session?.Title ?? "Locora root",
            ["{tempRoot}"] = _environmentPaths.TempRoot,
            ["{terminalCommandsFile}"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["{userRoot}"] = _environmentPaths.UserRoot,
            ["{workingDirectory}"] = workingDirectory
        };

        if (Uri.TryCreate(projectUrl, UriKind.Absolute, out var projectUri))
        {
            replacements["{projectHost}"] = projectUri.Host;
            replacements["{projectScheme}"] = projectUri.Scheme;
        }

        if (profile.Port is { } port)
        {
            replacements["{port}"] = port.ToString();
        }

        if (extraReplacements is not null)
        {
            foreach (var pair in extraReplacements)
            {
                replacements[pair.Key] = pair.Value;
            }
        }

        var resolved = template;
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

    private ProjectCard? ResolveLocalTunnelProject(LocalTunnelProfileCard profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ProjectName))
        {
            return FindProjectCard(profile.ProjectName);
        }

        if (NormalizeTerminalCommandScope(profile.Scope) != "project")
        {
            return null;
        }

        if (SelectedTerminalSession?.IsProjectScoped == true &&
            SelectedTerminalSession.LaunchProfile.EnvironmentVariables.TryGetValue("LOCORA_PROJECT_ROOT", out var selectedProjectRoot))
        {
            var selectedProject = Projects.FirstOrDefault(project =>
                string.Equals(project.Folder, selectedProjectRoot, StringComparison.OrdinalIgnoreCase));
            if (selectedProject is not null)
            {
                return selectedProject;
            }
        }

        return Projects.FirstOrDefault(project => project.IsPinned) ?? Projects.FirstOrDefault();
    }

    private ProjectCard? FindProjectCard(string value)
    {
        var normalized = value.Trim();
        return Projects.FirstOrDefault(project =>
            project.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            project.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            project.Folder.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractPortFromUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsDefaultPort
            ? uri.Port.ToString()
            : null;
    }

    private static string CleanShareUrl(string value)
    {
        return value.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'');
    }

    private static bool IsPublicShareUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1" ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(host, out var address))
        {
            return true;
        }

        if (System.Net.IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length != 4 ||
            (bytes[0] != 10 &&
                bytes[0] != 127 &&
                !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                !(bytes[0] == 192 && bytes[1] == 168));
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

    private string ResolveCustomToolTarget(CustomToolMenuItem tool)
    {
        var targetSession = NormalizeTerminalCommandScope(tool.Scope) == "project"
            ? TryGetProjectTerminalSessionForCommand()
            : SelectedTerminalSession;
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
            ["{customToolsFile}"] = _environmentPaths.CustomToolsSettingsFile,
            ["{dataRoot}"] = _environmentPaths.DataRoot,
            ["{locoraProfile}"] = GetTerminalLaunchProfileValue(launchProfile, "LOCORA_PROFILE", _lastSnapshot?.ActiveProfile ?? "Bootstrap"),
            ["{locoraRoot}"] = _environmentPaths.AppRoot,
            ["{logsRoot}"] = _environmentPaths.LogsRoot,
            ["{projectName}"] = projectName,
            ["{projectRoot}"] = projectRoot,
            ["{projectUrl}"] = projectUrl,
            ["{tempRoot}"] = _environmentPaths.TempRoot,
            ["{terminalCommandsFile}"] = _environmentPaths.TerminalCommandsSettingsFile,
            ["{userRoot}"] = _environmentPaths.UserRoot,
            ["{workingDirectory}"] = workingDirectory
        };

        var resolved = tool.Target;
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

    private string ResolveCustomToolPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        return Path.GetFullPath(Path.IsPathRooted(target)
            ? target
            : Path.Combine(_environmentPaths.AppRoot, target));
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

    private static string NormalizeCustomToolAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "terminal";
        }

        return action.Trim().ToLowerInvariant() switch
        {
            "url" => "url",
            "browser" => "url",
            "folder" => "folder",
            "directory" => "folder",
            "file" => "file",
            "editor" => "editor",
            "terminal" => "terminal",
            "command" => "terminal",
            "shell" => "terminal",
            _ => action.Trim().ToLowerInvariant()
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

    private bool TryResolveProfileTransferPath(out string resolvedPath, out string validationMessage)
    {
        resolvedPath = string.Empty;
        validationMessage = string.Empty;

        var candidate = (ProfileTransferPath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            validationMessage = "Enter a profile import/export JSON file path.";
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(_environmentPaths.ProfilesRoot, candidate));
            return true;
        }
        catch (Exception exception)
        {
            validationMessage = $"Profile import/export path is invalid: {exception.Message}";
            return false;
        }
    }

    private bool TryResolveConfigRestorePath(out string resolvedPath, out string validationMessage)
    {
        resolvedPath = string.Empty;
        validationMessage = string.Empty;

        var candidate = (ConfigRestorePath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            validationMessage = "Enter a config backup zip path to restore.";
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(ConfigBackupRootPath, candidate));
        }
        catch (Exception exception)
        {
            validationMessage = $"Config restore path is invalid: {exception.Message}";
            return false;
        }

        if (!string.Equals(Path.GetExtension(resolvedPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            validationMessage = $"Config restore expects a .zip archive: {resolvedPath}";
            return false;
        }

        return true;
    }

    private async Task<ConfigBackupArchiveResult> CreateConfigBackupArchiveAsync(string fileNamePrefix)
    {
        Directory.CreateDirectory(ConfigBackupRootPath);

        if (!Directory.Exists(ConfigRoot))
        {
            throw new DirectoryNotFoundException($"Configuration folder was not found: {ConfigRoot}");
        }

        var backupPath = CreateUniqueConfigBackupArchivePath(fileNamePrefix);
        var files = EnumerateConfigBackupFiles().ToList();

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(ConfigRoot, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }

            var manifest = archive.CreateEntry("locora-backup-manifest.txt", CompressionLevel.Fastest);
            await using var stream = manifest.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(CreateConfigBackupManifest(backupPath, files.Count));
        }

        return new ConfigBackupArchiveResult(backupPath, files.Count);
    }

    private string CreateUniqueConfigBackupArchivePath(string fileNamePrefix)
    {
        var prefix = string.IsNullOrWhiteSpace(fileNamePrefix)
            ? "locora-config"
            : fileNamePrefix.Trim();
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"-{attempt:D2}";
            var backupPath = Path.Combine(ConfigBackupRootPath, $"{prefix}-{timestamp}{suffix}.zip");
            if (!File.Exists(backupPath))
            {
                return backupPath;
            }
        }

        throw new IOException($"Could not create a unique config backup archive path in {ConfigBackupRootPath}.");
    }

    private IEnumerable<string> EnumerateConfigBackupFiles()
    {
        var configRoot = Path.GetFullPath(ConfigRoot);
        var backupRoot = Path.GetFullPath(ConfigBackupRootPath);

        foreach (var file in Directory.EnumerateFiles(configRoot, "*", SearchOption.AllDirectories))
        {
            if (IsPathWithinDirectory(file, backupRoot))
            {
                continue;
            }

            yield return Path.GetFullPath(file);
        }
    }

    private string CreateConfigBackupManifest(string backupPath, int fileCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Locora configuration backup");
        builder.AppendLine($"Created local: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Created UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        builder.AppendLine($"App root: {_environmentPaths.AppRoot}");
        builder.AppendLine($"Config root: {ConfigRoot}");
        builder.AppendLine($"Backup path: {backupPath}");
        builder.AppendLine($"File count: {fileCount}");
        builder.AppendLine("Scope: usr/config recursive contents plus this manifest.");
        return builder.ToString();
    }

    private int ExtractConfigBackupArchive(string archivePath, string extractRoot)
    {
        var extractedFileCount = 0;
        var fullExtractRoot = Path.GetFullPath(extractRoot);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalizedEntryName = entry.FullName.Replace('\\', '/');
            if (string.Equals(normalizedEntryName, "locora-backup-manifest.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Path.IsPathRooted(normalizedEntryName))
            {
                throw new InvalidDataException($"Config backup archive contains an absolute path entry: {entry.FullName}");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(fullExtractRoot, normalizedEntryName));
            if (!IsPathWithinDirectory(destinationPath, fullExtractRoot))
            {
                throw new InvalidDataException($"Config backup archive contains an unsafe path entry: {entry.FullName}");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
            extractedFileCount++;
        }

        return extractedFileCount;
    }

    private int ApplyExtractedConfigFiles(string extractRoot)
    {
        var restoredFileCount = 0;
        var fullExtractRoot = Path.GetFullPath(extractRoot);
        var fullConfigRoot = Path.GetFullPath(ConfigRoot);
        Directory.CreateDirectory(fullConfigRoot);

        foreach (var file in Directory.EnumerateFiles(fullExtractRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(fullExtractRoot, file);
            var destinationPath = Path.GetFullPath(Path.Combine(fullConfigRoot, relativePath));
            if (!IsPathWithinDirectory(destinationPath, fullConfigRoot))
            {
                throw new InvalidDataException($"Extracted config file resolved outside the configuration folder: {relativePath}");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destinationPath, overwrite: true);
            restoredFileCount++;
        }

        return restoredFileCount;
    }

    private void TryDeleteRestoreTempRoot(string restoreTempRoot)
    {
        try
        {
            var restoreTempBase = Path.GetFullPath(Path.Combine(_environmentPaths.TempRoot, "config-restore"));
            var fullRestoreTempRoot = Path.GetFullPath(restoreTempRoot);
            if (Directory.Exists(fullRestoreTempRoot) && IsPathWithinDirectory(fullRestoreTempRoot, restoreTempBase))
            {
                Directory.Delete(fullRestoreTempRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Temporary config restore folder could not be removed.");
        }
    }

    private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
    {
        var directory = EnsureTrailingDirectorySeparator(Path.GetFullPath(directoryPath));
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.Equals(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveLaragonRootPath(out string resolvedPath, out string validationMessage)
    {
        resolvedPath = string.Empty;
        validationMessage = string.Empty;

        var candidate = (LaragonRootPath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            validationMessage = "Enter the Laragon root path, for example C:\\laragon.";
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception)
        {
            validationMessage = $"Laragon root path is invalid: {exception.Message}";
            return false;
        }

        if (!Directory.Exists(resolvedPath))
        {
            validationMessage = $"Laragon root folder was not found: {resolvedPath}";
            return false;
        }

        return true;
    }

    private LaragonImportPlan BuildLaragonImportPlan(string rootPath, ProjectSettingsDocument currentDocument)
    {
        var wwwRoot = ResolveLaragonWwwRoot(rootPath);
        var configRoot = ResolveLaragonConfigRoot(rootPath, wwwRoot);
        var detection = DetectLaragonDomainSuffix(configRoot);
        var ignoredNames = currentDocument.LocoraProjects.IgnoredDirectoryNames
            .Concat(DefaultIgnoredDirectoryNames)
            .Concat(["tmp", "temp"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projects = Directory.EnumerateDirectories(wwwRoot)
            .Where(directory => IsImportableLaragonProjectDirectory(directory, ignoredNames))
            .OrderBy(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
            .Select(directory => CreateLaragonImportedProject(directory, detection.DomainSuffix, currentDocument.LocoraProjects.ProjectOverrides))
            .ToList();

        return new LaragonImportPlan(configRoot, wwwRoot, detection.DomainSuffix, "http", detection.Source, projects);
    }

    private void ApplyLaragonImportPlan(LaragonImportPlan plan)
    {
        ReplaceCollection(LaragonImportProjects, plan.Projects.Select(project => project.ToCard()));
        NotifyLaragonImportCollectionProperties();
    }

    private void NotifyLaragonImportCollectionProperties()
    {
        OnPropertyChanged(nameof(HasLaragonImportProjects));
        OnPropertyChanged(nameof(ShowLaragonImportEmptyState));
    }

    private static string CreateLaragonImportSummary(LaragonImportPlan plan)
    {
        if (plan.Projects.Count == 0)
        {
            return $"No projects found under {plan.WwwRoot}.";
        }

        var updateCount = plan.Projects.Count(project => project.Status.StartsWith("Will update", StringComparison.OrdinalIgnoreCase));
        var addCount = plan.Projects.Count - updateCount;
        return $"{plan.Projects.Count} project{(plan.Projects.Count == 1 ? string.Empty : "s")} ready from {plan.WwwRoot}; {addCount} add, {updateCount} update; domains use *.{plan.DomainSuffix}.";
    }

    private static string ResolveLaragonWwwRoot(string rootPath)
    {
        var childWww = Path.Combine(rootPath, "www");
        if (Directory.Exists(childWww))
        {
            return Path.GetFullPath(childWww);
        }

        if (Path.GetFileName(rootPath).Equals("www", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(rootPath);
        }

        throw new DirectoryNotFoundException($"Laragon www folder was not found under {rootPath}.");
    }

    private static string ResolveLaragonConfigRoot(string rootPath, string wwwRoot)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullWww = Path.GetFullPath(wwwRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullRoot.Equals(fullWww, StringComparison.OrdinalIgnoreCase))
        {
            return fullRoot;
        }

        return Directory.GetParent(fullWww)?.FullName ?? fullWww;
    }

    private LaragonImportedProject CreateLaragonImportedProject(
        string projectPath,
        string domainSuffix,
        IReadOnlyList<ProjectOverrideSettings> existingOverrides)
    {
        var name = Path.GetFileName(projectPath);
        var key = CreateImportSlug(name);
        var documentRoot = DetectLaragonDocumentRoot(projectPath);
        var framework = DetectLaragonFramework(projectPath);
        var runtime = DetectLaragonRuntime(projectPath, framework);
        var tags = CreateLaragonTags(framework);
        var status = HasMatchingProjectOverride(existingOverrides, projectPath, key, name)
            ? "Will update existing override"
            : "Will add project override";

        return new LaragonImportedProject(
            key,
            name,
            Path.GetFullPath(projectPath),
            $"{key}.{domainSuffix}",
            documentRoot,
            runtime,
            framework,
            $"Imported from Laragon: {projectPath}",
            tags,
            status);
    }

    private bool HasMatchingProjectOverride(
        IReadOnlyList<ProjectOverrideSettings> existingOverrides,
        string projectPath,
        string key,
        string name)
    {
        return existingOverrides.Any(existing => MatchesLaragonImportOverride(existing, projectPath, key, name));
    }

    private bool MatchesLaragonImportOverride(ProjectOverrideSettings existing, string projectPath, string key, string name)
    {
        if (!string.IsNullOrWhiteSpace(existing.Path) &&
            TryResolveProjectOverridePath(existing.Path, out var existingPath) &&
            existingPath.Equals(Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (!string.IsNullOrWhiteSpace(existing.Key) && existing.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(existing.Name) && existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResolveProjectOverridePath(string candidate, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(_environmentPaths.ProjectRoot, candidate));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<ProjectOverrideSettings> MergeLaragonProjectOverrides(
        IReadOnlyList<ProjectOverrideSettings> existingOverrides,
        IReadOnlyList<LaragonImportedProject> importedProjects,
        out int addedCount,
        out int updatedCount)
    {
        var merged = existingOverrides.ToList();
        addedCount = 0;
        updatedCount = 0;

        foreach (var importedProject in importedProjects)
        {
            var replacement = importedProject.ToProjectOverride();
            var existingIndex = merged.FindIndex(existing => MatchesLaragonImportOverride(
                existing,
                importedProject.SourcePath,
                importedProject.Key,
                importedProject.Name));

            if (existingIndex >= 0)
            {
                merged[existingIndex] = replacement;
                updatedCount++;
            }
            else
            {
                merged.Add(replacement);
                addedCount++;
            }
        }

        return merged;
    }

    private string? CreateLaragonProjectSettingsBackup()
    {
        if (!File.Exists(_environmentPaths.ProjectsSettingsFile))
        {
            return null;
        }

        var backupRoot = Path.Combine(_environmentPaths.ConfigRoot, "migration-backups");
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, $"projects-before-laragon-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(_environmentPaths.ProjectsSettingsFile, backupPath, overwrite: false);
        return backupPath;
    }

    private static bool IsImportableLaragonProjectDirectory(string directory, ISet<string> ignoredNames)
    {
        var name = Path.GetFileName(directory);
        return !string.IsNullOrWhiteSpace(name) &&
            !name.StartsWith(".", StringComparison.Ordinal) &&
            !ignoredNames.Contains(name);
    }

    private static string DetectLaragonDocumentRoot(string projectPath)
    {
        var publicRoot = Path.Combine(projectPath, "public");
        return Directory.Exists(publicRoot) && HasAnyIndexFile(publicRoot)
            ? "public"
            : string.Empty;
    }

    private static bool HasAnyIndexFile(string directory)
    {
        return DefaultIndexFileNames.Any(fileName => File.Exists(Path.Combine(directory, fileName)));
    }

    private static string DetectLaragonFramework(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "artisan")))
        {
            return "Laravel";
        }

        if (File.Exists(Path.Combine(projectPath, "wp-config.php")) ||
            Directory.Exists(Path.Combine(projectPath, "wp-content")))
        {
            return "WordPress";
        }

        if (File.Exists(Path.Combine(projectPath, "symfony.lock")))
        {
            return "Symfony";
        }

        if (File.Exists(Path.Combine(projectPath, "next.config.js")) ||
            File.Exists(Path.Combine(projectPath, "next.config.mjs")) ||
            Directory.Exists(Path.Combine(projectPath, ".next")))
        {
            return "Next.js";
        }

        if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
            File.Exists(Path.Combine(projectPath, "requirements.txt")))
        {
            return "Python";
        }

        if (File.Exists(Path.Combine(projectPath, "pom.xml")) ||
            File.Exists(Path.Combine(projectPath, "build.gradle")) ||
            File.Exists(Path.Combine(projectPath, "build.gradle.kts")) ||
            File.Exists(Path.Combine(projectPath, "mvnw")) ||
            File.Exists(Path.Combine(projectPath, "gradlew")))
        {
            return "Java";
        }

        if (File.Exists(Path.Combine(projectPath, "composer.json")))
        {
            return "Composer";
        }

        if (File.Exists(Path.Combine(projectPath, "package.json")))
        {
            return "Node";
        }

        if (File.Exists(Path.Combine(projectPath, "index.php")))
        {
            return "PHP";
        }

        return "Static";
    }

    private static string DetectLaragonRuntime(string projectPath, string framework)
    {
        if (framework is "Laravel" or "WordPress" or "Symfony" or "Composer" or "PHP")
        {
            return "PHP";
        }

        if (framework is "Next.js" or "Node" || File.Exists(Path.Combine(projectPath, "package.json")))
        {
            return "Node.js";
        }

        if (framework == "Python")
        {
            return "Python";
        }

        if (framework == "Java")
        {
            return "Java";
        }

        return "Static";
    }

    private static IReadOnlyList<string> CreateLaragonTags(string framework)
    {
        return string.IsNullOrWhiteSpace(framework) || framework.Equals("Static", StringComparison.OrdinalIgnoreCase)
            ? ["laragon", "imported"]
            : ["laragon", "imported", framework.ToLowerInvariant()];
    }

    private static string CreateImportSlug(string name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        normalized = Regex.Replace(normalized, "[^a-z0-9-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    private static LaragonDomainDetection DetectLaragonDomainSuffix(string rootPath)
    {
        foreach (var settingsFile in EnumerateLaragonSettingsFiles(rootPath))
        {
            if (!File.Exists(settingsFile))
            {
                continue;
            }

            foreach (var line in File.ReadLines(settingsFile))
            {
                if (TryExtractLaragonDomainSuffixFromSettingsLine(line, out var domainSuffix))
                {
                    return new LaragonDomainDetection(domainSuffix, settingsFile);
                }
            }
        }

        foreach (var vhostDirectory in EnumerateLaragonVHostDirectories(rootPath))
        {
            if (!Directory.Exists(vhostDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(vhostDirectory, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (TryExtractLaragonDomainSuffixFromVHost(file, out var domainSuffix))
                {
                    return new LaragonDomainDetection(domainSuffix, vhostDirectory);
                }
            }
        }

        return new LaragonDomainDetection("test", "default Laragon convention");
    }

    private static IEnumerable<string> EnumerateLaragonSettingsFiles(string rootPath)
    {
        yield return Path.Combine(rootPath, "usr", "laragon.ini");
        yield return Path.Combine(rootPath, "usr", "settings.ini");
        yield return Path.Combine(rootPath, "laragon.ini");
    }

    private static IEnumerable<string> EnumerateLaragonVHostDirectories(string rootPath)
    {
        yield return Path.Combine(rootPath, "etc", "nginx", "sites-enabled");
        yield return Path.Combine(rootPath, "etc", "apache2", "sites-enabled");
        yield return Path.Combine(rootPath, "etc", "apache2", "sites-available");
    }

    private static bool TryExtractLaragonDomainSuffixFromSettingsLine(string line, out string domainSuffix)
    {
        domainSuffix = string.Empty;
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.StartsWith(';') ||
            trimmed.StartsWith('#') ||
            trimmed.StartsWith('[') ||
            !trimmed.Contains('='))
        {
            return false;
        }

        var parts = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
        var key = parts[0];
        var value = parts[1];
        if (!key.Contains("tld", StringComparison.OrdinalIgnoreCase) &&
            !key.Contains("domain", StringComparison.OrdinalIgnoreCase) &&
            !key.Contains("hostname", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryNormalizeLaragonDomainSuffix(value, out domainSuffix);
    }

    private static bool TryExtractLaragonDomainSuffixFromVHost(string file, out string domainSuffix)
    {
        domainSuffix = string.Empty;
        string content;
        try
        {
            content = File.ReadAllText(file);
        }
        catch
        {
            return false;
        }

        foreach (Match match in Regex.Matches(content, @"\b(?:server_name|ServerName)\s+([^;\s]+)", RegexOptions.IgnoreCase))
        {
            if (TryNormalizeLaragonDomainSuffix(match.Groups[1].Value, out domainSuffix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeLaragonDomainSuffix(string candidate, out string domainSuffix)
    {
        domainSuffix = string.Empty;
        var value = (candidate ?? string.Empty).Trim().Trim('"', '\'').Trim('.');
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value
            .Replace("{name}", "project", StringComparison.OrdinalIgnoreCase)
            .Replace("{project}", "project", StringComparison.OrdinalIgnoreCase)
            .Replace("*.", "project.", StringComparison.OrdinalIgnoreCase);

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = uri.Host;
        }

        value = value.Trim().Trim('.').ToLowerInvariant();
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var firstDotIndex = value.IndexOf('.');
        if (firstDotIndex > 0)
        {
            value = value[(firstDotIndex + 1)..];
        }

        if (value.IndexOfAny(new[] { '/', '\\', '?', '#', ':' }) >= 0 ||
            value.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(value) == UriHostNameType.Unknown)
        {
            return false;
        }

        domainSuffix = value;
        return true;
    }

    private static string ResolveDefaultLaragonRootPath()
    {
        return Directory.Exists(@"C:\laragon") ? @"C:\laragon" : string.Empty;
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
                IgnoredDirectoryNames: DefaultIgnoredDirectoryNames,
                ProjectOverrides: Array.Empty<ProjectOverrideSettings>()));
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
        var url = values.TryGetValue("URL", out var urlText) && !string.IsNullOrWhiteSpace(urlText)
            ? urlText
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

    private static string CreateLocalTunnelTerminalTitle(LocalTunnelProfileCard profile, ProjectCard? project)
    {
        return project is null
            ? $"Tunnel: {profile.DisplayName}"
            : $"Tunnel: {profile.DisplayName} ({project.Name})";
    }

    private static string CreatePortListLabel(IEnumerable<int> ports)
    {
        var distinctPorts = ports
            .Where(port => port > 0)
            .Distinct()
            .OrderBy(port => port)
            .Select(port => port.ToString())
            .ToArray();

        return distinctPorts.Length == 0
            ? "No configured ports"
            : $"TCP {string.Join(", ", distinctPorts)}";
    }

    private static string CreateServicePortSummary(IReadOnlyList<ServiceDescriptor> services)
    {
        if (services.Count == 0)
        {
            return "No service ports are currently reported by the supervisor.";
        }

        var runningServices = services
            .Where(service => service.State == ServiceState.Running)
            .Select(service => $"{service.DisplayName} {service.Port}")
            .ToArray();

        return runningServices.Length == 0
            ? $"{services.Count} configured service port{(services.Count == 1 ? string.Empty : "s")} reported; none are currently running."
            : $"Running service ports: {string.Join(", ", runningServices)}.";
    }

    private static string CreateTunnelProfileSummary(IReadOnlyList<LocalTunnelProfileCard> profiles)
    {
        if (profiles.Count == 0)
        {
            return "No enabled local tunnel profiles are configured.";
        }

        var names = profiles
            .Take(4)
            .Select(profile => profile.DisplayName)
            .ToArray();
        var suffix = profiles.Count > names.Length ? $", +{profiles.Count - names.Length} more" : string.Empty;
        return $"Enabled tunnel profiles: {string.Join(", ", names)}{suffix}.";
    }

    private static string CreateTunnelTargetLabel(IReadOnlyList<LocalTunnelProfileCard> profiles)
    {
        var ports = profiles
            .Select(profile => profile.Port ?? TryGetPortFromUrl(profile.LocalUrl))
            .Where(port => port is > 0)
            .Select(port => port!.Value)
            .ToArray();

        if (ports.Length > 0)
        {
            return CreatePortListLabel(ports);
        }

        return profiles.Count == 0
            ? "No tunnel targets"
            : $"{profiles.Count} tunnel target{(profiles.Count == 1 ? string.Empty : "s")}";
    }

    private static int? TryGetPortFromUrl(string localUrl)
    {
        if (!Uri.TryCreate(localUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.IsDefaultPort)
        {
            return uri.Port;
        }

        return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? 80
            : uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                ? 443
                : null;
    }

    private static bool IsDataService(ServiceDescriptor service)
    {
        var key = service.Key.ToLowerInvariant();
        return key.Contains("mysql", StringComparison.Ordinal) ||
            key.Contains("mariadb", StringComparison.Ordinal) ||
            key.Contains("postgres", StringComparison.Ordinal) ||
            key.Contains("redis", StringComparison.Ordinal) ||
            key.Contains("memcached", StringComparison.Ordinal) ||
            key.Contains("mailpit", StringComparison.Ordinal);
    }

    private static string EnsureTrailingSlash(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : $"{value}/";
    }

    private static string EnsureTrailingDirectorySeparator(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.EndsWith(Path.DirectorySeparatorChar) ||
            value.EndsWith(Path.AltDirectorySeparatorChar)
                ? value
                : $"{value}{Path.DirectorySeparatorChar}";
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

    private sealed record ConfigBackupArchiveResult(string Path, int FileCount);

    private sealed record LaragonImportPlan(
        string RootPath,
        string WwwRoot,
        string DomainSuffix,
        string DefaultScheme,
        string DomainSource,
        IReadOnlyList<LaragonImportedProject> Projects);

    private sealed record LaragonImportedProject(
        string Key,
        string Name,
        string SourcePath,
        string Domain,
        string DocumentRoot,
        string Runtime,
        string Framework,
        string Description,
        IReadOnlyList<string> Tags,
        string Status)
    {
        public LaragonImportProjectCard ToCard()
        {
            return new LaragonImportProjectCard(
                Key,
                Name,
                SourcePath,
                Domain,
                DocumentRoot,
                Runtime,
                Framework,
                Status,
                Tags);
        }

        public ProjectOverrideSettings ToProjectOverride()
        {
            return new ProjectOverrideSettings(
                Key,
                Name,
                SourcePath,
                Domain,
                "http",
                DocumentRoot,
                Runtime,
                Framework,
                Description,
                JsonSerializer.SerializeToElement(Tags));
        }
    }

    private sealed record LaragonDomainDetection(string DomainSuffix, string Source);

    private sealed record ProjectSettingsDocument(ProjectSettingsSection LocoraProjects);

    private sealed record TerminalCommandsDocument(TerminalCommandsSection LocoraTerminal);

    private sealed record CustomToolsDocument(CustomToolsSection LocoraTools);

    private sealed record LocalTunnelsDocument(LocalTunnelsSection LocoraTunnels);

    private sealed record TerminalCommandsSection(IReadOnlyList<TerminalCommandSettings> Commands);

    private sealed record CustomToolsSection(IReadOnlyList<CustomToolSettings> Tools);

    private sealed record LocalTunnelsSection(IReadOnlyList<LocalTunnelSettings> Profiles);

    private sealed record TerminalCommandSettings(
        string Key,
        string DisplayName,
        string Alias,
        string Description,
        string CommandText,
        string Scope,
        IReadOnlyList<string> Tags);

    private sealed record CustomToolSettings(
        string Key,
        string DisplayName,
        string Description,
        string Action,
        string Target,
        string Scope,
        IReadOnlyList<string> Tags,
        bool? IsEnabled);

    private sealed record LocalTunnelSettings(
        string Key,
        string DisplayName,
        string Provider,
        string Description,
        string CommandText,
        string Scope,
        string ProjectName,
        string LocalUrl,
        int? Port,
        IReadOnlyList<string> Tags,
        bool? IsEnabled);

    private sealed record ProjectSettingsSection(
        bool EnableAutoDiscovery,
        bool GenerateNginxVHosts,
        bool GenerateApacheVHosts,
        bool GenerateHostsPreview,
        string DomainSuffix,
        string DefaultScheme,
        IReadOnlyList<string> IndexFileNames,
        IReadOnlyList<string> IgnoredDirectoryNames,
        IReadOnlyList<ProjectOverrideSettings> ProjectOverrides);

    private sealed record ProjectOverrideSettings(
        string Key,
        string Name,
        string Path,
        string Domain,
        string Scheme,
        string DocumentRoot,
        string Runtime,
        string Framework,
        string Description,
        JsonElement Tags);

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
