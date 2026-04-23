using Locora.App.Contracts;
using Locora.Infrastructure.Services;

namespace Locora.Supervisor.Services;

public sealed class SupervisorStateStore
{
    private readonly ManagedServiceRegistry _registry;
    private readonly ProjectDiscoveryService _projectDiscovery;
    private readonly ProjectConfigurationWriter _projectConfigurationWriter;
    private readonly HostsFileManager _hostsFileManager;
    private readonly ElevationService _elevationService;
    private readonly SupervisorPrivilegeService _privilegeService;
    private readonly NginxConfigValidator _nginxConfigValidator;
    private readonly LocalSslService _localSslService;
    private readonly PortDiagnosticsService _portDiagnosticsService;
    private readonly PermissionDiagnosticsService _permissionDiagnosticsService;
    private readonly PackageSourceRegistry _packageSourceRegistry;
    private readonly PackageDownloadManager _packageDownloadManager;
    private readonly StackProfileRegistry _stackProfileRegistry;
    private readonly RuntimeRepairService _runtimeRepairService;
    private readonly string _appRoot;

    public SupervisorStateStore(
        ManagedServiceRegistry registry,
        ProjectDiscoveryService projectDiscovery,
        ProjectConfigurationWriter projectConfigurationWriter,
        HostsFileManager hostsFileManager,
        ElevationService elevationService,
        SupervisorPrivilegeService privilegeService,
        NginxConfigValidator nginxConfigValidator,
        LocalSslService localSslService,
        PortDiagnosticsService portDiagnosticsService,
        PermissionDiagnosticsService permissionDiagnosticsService,
        PackageSourceRegistry packageSourceRegistry,
        PackageDownloadManager packageDownloadManager,
        StackProfileRegistry stackProfileRegistry,
        RuntimeRepairService runtimeRepairService,
        Locora.Application.Abstractions.IEnvironmentPaths environmentPaths)
    {
        _registry = registry;
        _projectDiscovery = projectDiscovery;
        _projectConfigurationWriter = projectConfigurationWriter;
        _hostsFileManager = hostsFileManager;
        _elevationService = elevationService;
        _privilegeService = privilegeService;
        _nginxConfigValidator = nginxConfigValidator;
        _localSslService = localSslService;
        _portDiagnosticsService = portDiagnosticsService;
        _permissionDiagnosticsService = permissionDiagnosticsService;
        _packageSourceRegistry = packageSourceRegistry;
        _packageDownloadManager = packageDownloadManager;
        _stackProfileRegistry = stackProfileRegistry;
        _runtimeRepairService = runtimeRepairService;
        _appRoot = environmentPaths.AppRoot;
    }

    public async Task<EnvironmentSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var statuses = await _registry.GetStatusesAsync(cancellationToken);
        var projects = await GenerateProjectConfigurationAsync(cancellationToken);
        var portDiagnostics = _portDiagnosticsService.GetDiagnostics(statuses);
        var permissionDiagnostics = _permissionDiagnosticsService.GetDiagnostics();
        var packageRegistry = await _packageSourceRegistry.GetSnapshotAsync(cancellationToken);
        var packageDownloads = await _packageDownloadManager.GetSnapshotAsync(cancellationToken);
        var runtimePackages = await _packageDownloadManager.GetRuntimeSnapshotAsync(cancellationToken);
        var toolPackages = await _packageDownloadManager.GetToolSnapshotAsync(cancellationToken);
        var stackProfiles = await _stackProfileRegistry.GetSnapshotAsync(_registry.Services, cancellationToken);
        var packageCompatibility = await _packageDownloadManager.GetCompatibilitySnapshotAsync(CreatePackageCompatibilityRequirements(), cancellationToken);
        var validationResults = CreateValidationResults(packageCompatibility, stackProfiles);
        var sslStatus = await _localSslService.GetStatusAsync(projects, cancellationToken);

        return new EnvironmentSnapshotDto(
            _appRoot,
            stackProfiles.ActiveProfileName,
            statuses.Select(Map).ToList(),
            projects.Select(Map).ToList(),
            CreateIssues(statuses, projects, validationResults, portDiagnostics, permissionDiagnostics, packageRegistry, packageDownloads, runtimePackages, toolPackages, stackProfiles, sslStatus),
            validationResults,
            portDiagnostics.Select(Map).ToList(),
            permissionDiagnostics.Select(Map).ToList(),
            packageRegistry.Sources.Select(Map).ToList(),
            Map(packageRegistry),
            packageDownloads.Downloads.Select(Map).ToList(),
            Map(packageDownloads),
            runtimePackages.Runtimes.Select(Map).ToList(),
            Map(runtimePackages),
            toolPackages.Tools.Select(Map).ToList(),
            Map(toolPackages),
            stackProfiles.Profiles.Select(Map).ToList(),
            Map(stackProfiles),
            Map(sslStatus),
            DateTimeOffset.UtcNow,
            _privilegeService.IsElevated(),
            Environment.ProcessId);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        await GenerateProjectConfigurationAsync(cancellationToken);
        var activeProfile = await _stackProfileRegistry.GetActiveProfileAsync(_registry.Services, cancellationToken);
        await _registry.StartServicesAsync(activeProfile.ServiceKeys, cancellationToken);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        await _registry.StopAllAsync(cancellationToken);
    }

    public async Task StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await GenerateProjectConfigurationAsync(cancellationToken);
        await _registry.StartAsync(serviceKey, cancellationToken);
    }

    public async Task StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await _registry.StopAsync(serviceKey, cancellationToken);
    }

    public async Task ApplyHostsPreviewAsync(CancellationToken cancellationToken = default)
    {
        await GenerateProjectConfigurationAsync(cancellationToken);
        await _hostsFileManager.ApplyPreviewAsync(cancellationToken);
    }

    public async Task RollbackHostsAsync(CancellationToken cancellationToken = default)
    {
        await _hostsFileManager.RollbackLatestAsync(cancellationToken);
    }

    public async Task RestartElevatedAsync(CancellationToken cancellationToken = default)
    {
        await _elevationService.RestartElevatedAsync(cancellationToken);
    }

    public async Task ValidateNginxConfigAsync(CancellationToken cancellationToken = default)
    {
        await GenerateProjectConfigurationAsync(cancellationToken);
        await _nginxConfigValidator.ValidateAsync(cancellationToken);
    }

    public async Task RepairRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeRepairService.RepairCommonIssuesAsync(cancellationToken);
    }

    public async Task RepairDomainsAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeRepairService.RepairDomainsAsync(cancellationToken);
    }

    public async Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await _runtimeRepairService.RepairServiceAsync(serviceKey, cancellationToken);
    }

    public async Task SyncPackageDownloadsAsync(CancellationToken cancellationToken = default)
    {
        await _packageDownloadManager.SyncDownloadsAsync(cancellationToken);
    }

    public async Task ExtractPackageArchivesAsync(CancellationToken cancellationToken = default)
    {
        await _packageDownloadManager.ExtractArchivesAsync(cancellationToken);
    }

    public async Task InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default)
    {
        await _packageDownloadManager.InstallOrUpdatePackagesAsync(cancellationToken);
    }

    public async Task RemovePackageInstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await _packageDownloadManager.RemoveInstalledPackageAsync(packageId, cancellationToken);
    }

    public async Task SelectRuntimeVersionAsync(string selectionKey, CancellationToken cancellationToken = default)
    {
        await _packageDownloadManager.SelectRuntimeVersionAsync(selectionKey, cancellationToken);
    }

    public async Task SelectStackProfileAsync(string profileKey, CancellationToken cancellationToken = default)
    {
        var result = await _stackProfileRegistry.SelectProfileAsync(profileKey, _registry.Services, cancellationToken);
        await _packageDownloadManager.ApplyPackageSelectionsAsync(result.Profile.PackageSelections, cancellationToken);
    }

    public async Task RepairLocalSslAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        await _localSslService.RepairAsync(projects, cancellationToken);
        await GenerateProjectConfigurationAsync(cancellationToken);
    }

    public async Task GenerateLocalSslAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        await _localSslService.GenerateAsync(projects, cancellationToken);
        await GenerateProjectConfigurationAsync(cancellationToken);
    }

    public async Task TrustLocalSslCaAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        await _localSslService.TrustCurrentUserAsync(projects, cancellationToken);
    }

    public async Task RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        await _localSslService.RemoveCurrentUserTrustAsync(projects, cancellationToken);
    }

    private IReadOnlyList<HealthIssueDto> CreateIssues(
        IReadOnlyList<ManagedServiceStatus> statuses,
        IReadOnlyList<DiscoveredProject> projects,
        IReadOnlyList<ValidationResultDto> validationResults,
        IReadOnlyList<PortDiagnosticSnapshot> portDiagnostics,
        IReadOnlyList<PermissionDiagnosticSnapshot> permissionDiagnostics,
        PackageSourceRegistrySnapshot packageRegistry,
        PackageDownloadManagerSnapshot packageDownloads,
        RuntimePackageManagerSnapshot runtimePackages,
        ToolPackageManagerSnapshot toolPackages,
        StackProfileRegistrySnapshot stackProfiles,
        LocalSslStatusSnapshot sslStatus)
    {
        var issues = new List<HealthIssueDto>();

        if (statuses.Count == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "No services configured",
                    "No managed services were loaded from usr/config/services.json.",
                    "Add service definitions for Nginx, Apache, MariaDB, PostgreSQL, Redis, Memcached, Mailpit, and related tools."));
            return issues;
        }

        if (projects.Count == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "No projects discovered",
                    "Locora scanned the project root but did not find any project folders yet.",
                    "Create or copy a project folder into www, then refresh the dashboard."));
        }

        foreach (var collision in GetDomainCollisions(projects))
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    $"Domain collision: {collision.Host}",
                    $"{string.Join(", ", collision.Projects.Select(project => project.Name))} all resolve to {collision.Host}, so Locora skipped hosts and vhost generation for that hostname.",
                    "Rename one of the project folders or add/update a .locora.json domain override so each project has a unique hostname, then refresh."));
        }

        foreach (var status in statuses.Where(status => !status.ExecutableExists))
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    $"{status.DisplayName} binary missing",
                    status.Note ?? $"{status.DisplayName} is missing its executable path.",
                    $"Install or unpack {status.DisplayName} into the expected bin directory, then refresh the dashboard."));
        }

        foreach (var status in statuses.Where(status => status.ExecutableExists && status.State == "Error" && !IsExternalPortOwner(status)))
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    $"{status.DisplayName} is unhealthy",
                    status.Note ?? $"{status.DisplayName} stopped unexpectedly or failed its crash-recovery policy.",
                    "Use Repair on the service card or Repair Runtime Configs, then inspect logs and start the service again."));
        }

        foreach (var status in statuses.Where(status => status.State == "Starting"))
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    $"{status.DisplayName} is still starting",
                    status.Note ?? $"{status.DisplayName} has not opened its configured port yet.",
                    "Inspect the service stdout/stderr logs and use Repair Runtime Configs if generated configs look stale."));
        }

        foreach (var validation in validationResults.Where(validation => !validation.IsValid))
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    $"{validation.DisplayName} config validation failed",
                    validation.Summary,
                    validation.Key.Equals("package_service_compatibility", StringComparison.OrdinalIgnoreCase)
                        ? "Open Settings to align package selections with service versions, then run Install or Update Active Packages."
                        : "Use Repair Runtime Configs from Services or Domains & Hosts, then review the validation output before restarting."));
        }

        foreach (var diagnostic in portDiagnostics.Where(ShouldSurfacePortDiagnostic))
        {
            issues.Add(
                new HealthIssueDto(
                    diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ? "Error" : "Warning",
                    $"{diagnostic.DisplayName} needs attention",
                    diagnostic.Summary,
                    diagnostic.SuggestedAction));
        }

        foreach (var diagnostic in permissionDiagnostics.Where(ShouldSurfacePermissionDiagnostic))
        {
            issues.Add(
                new HealthIssueDto(
                    diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ? "Error" : "Warning",
                    $"{diagnostic.DisplayName} needs attention",
                    diagnostic.Summary,
                    diagnostic.SuggestedAction));
        }

        if (packageRegistry.EnabledSourceCount == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "No package sources enabled",
                    "Locora could not catalog any runtime or tool packages because every package source is disabled.",
                    "Enable at least one source in sources.json, then refresh diagnostics."));
        }

        foreach (var source in packageRegistry.Sources.Where(source => source.State.Equals("Error", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    $"Package source error: {source.DisplayName}",
                    source.Summary,
                    source.Details));
        }

        if (packageRegistry.EnabledSourceCount > 0 && packageRegistry.ReadySourceCount == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Package registry is not ready",
                    packageRegistry.Summary,
                    packageRegistry.Details));
        }

        if (packageDownloads.ChecksumMismatchCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    "Package checksum validation failed",
                    packageDownloads.Summary,
                    "Replace invalid cache files or correct manifest Sha256 values before installing cached runtimes or tools."));
        }
        else if (packageDownloads.ErrorCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    "Package download plan has errors",
                    packageDownloads.Summary,
                    packageDownloads.Details));
        }
        else if (packageDownloads.MissingCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Package artifacts are not staged",
                    packageDownloads.Summary,
                    "Add local archives or manifest download URLs, then run Install or Update Active Packages from Settings."));
        }
        else if (packageDownloads.PendingCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "Package downloads are pending sync",
                    packageDownloads.Summary,
                    "Run Install or Update Active Packages from Settings, or use Sync Package Downloads when you only want to warm the cache."));
        }

        if (packageDownloads.ExtractionErrorCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    "Package archive extraction needs attention",
                    packageDownloads.Summary,
                    "Repair the cached archive, supported archive type, or active runtime path, then run Extract Package Archives again."));
        }
        else if (packageDownloads.PendingExtractionCount > 0 && packageDownloads.CachedCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "Cached packages are ready to extract",
                    packageDownloads.Summary,
                    "Run Install or Update Active Packages from Settings, or use Extract Package Archives when the cache is already primed."));
        }

        if (packageDownloads.UnverifiedChecksumCount > 0 && packageDownloads.CachedCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Cached packages are not checksum-verified yet",
                    packageDownloads.Summary,
                    "Add Sha256 values to package manifest versions, then refresh so Locora can verify cached artifacts."));
        }

        if (runtimePackages.AttentionCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Runtime versions need attention",
                    runtimePackages.Summary,
                    "Open Settings and run Install or Update Active Packages, or switch selected PHP, Node.js, Python, or Java versions back to an installed version."));
        }
        else if (runtimePackages.RuntimeCount > 0 && runtimePackages.ActiveCount == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "Runtime versions are cataloged but inactive",
                    runtimePackages.Summary,
                    "Open Settings to select a PHP, Node.js, Python, or Java version, then install or extract the active package selections."));
        }

        if (toolPackages.AttentionCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Tool packages need attention",
                    toolPackages.Summary,
                    "Open Settings and run Install or Update Active Packages, or remove tool selections that are no longer needed."));
        }

        if (stackProfiles.InvalidProfileCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Stack profiles need attention",
                    stackProfiles.Summary,
                    "Open Settings and fix profiles.json so profile service keys match configured services."));
        }

        if (sslStatus.CaExists && sslStatus.TrustSupported && !sslStatus.IsCurrentUserTrusted)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Local SSL CA is not trusted",
                    sslStatus.Summary,
                    "Open Domains & Hosts and trust the Locora certificate authority to remove browser warnings for generated HTTPS sites."));
        }

        if (!sslStatus.CaExists && projects.Count > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "Local SSL has not been generated",
                    sslStatus.Summary,
                    "Open Domains & Hosts to generate certificates for discovered projects and enable local HTTPS."));
        }

        if (sslStatus.MissingProjectCertificateCount > 0 || sslStatus.ExpiredProjectCertificateCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Error",
                    "Local SSL certificates need repair",
                    sslStatus.Summary,
                    "Open Diagnostics or Domains & Hosts and run Repair Local SSL to recreate missing, expired, invalid, or stale project certificates."));
        }
        else if (sslStatus.ExpiringProjectCertificateCount > 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Warning",
                    "Local SSL certificates expire soon",
                    sslStatus.Summary,
                    "Run Repair Local SSL to renew project certificates before they expire."));
        }

        if (issues.Count == 0)
        {
            issues.Add(
                new HealthIssueDto(
                    "Info",
                    "Runtime-backed supervisor active",
                    "Locora is now probing configured services from real executable and port state instead of serving mock service cards.",
                    "Next, sync package downloads, extract cached archives, or continue into install/update automation."));
        }

        return issues;
    }

    private static ServiceStatusDto Map(ManagedServiceStatus status)
    {
        return new ServiceStatusDto(
            status.Key,
            status.DisplayName,
            status.Version,
            status.Port,
            status.State,
            status.AutoStart,
            status.Note);
    }

    private static PackageSourceStatusDto Map(PackageSourceStatusSnapshot source)
    {
        return new PackageSourceStatusDto(
            source.Id,
            source.DisplayName,
            source.Kind,
            source.State,
            source.Channel,
            source.Priority,
            source.ManifestPath,
            source.PackageCount,
            source.VersionCount,
            source.Summary,
            source.Details,
            source.IsEnabled);
    }

    private static PackageRegistrySummaryDto Map(PackageSourceRegistrySnapshot registry)
    {
        return new PackageRegistrySummaryDto(
            registry.EnabledSourceCount,
            registry.ReadySourceCount,
            registry.ErrorSourceCount,
            registry.PackageCount,
            registry.VersionCount,
            registry.Summary,
            registry.Details);
    }

    private static PackageDownloadStatusDto Map(PackageDownloadStatusSnapshot download)
    {
        return new PackageDownloadStatusDto(
            download.PackageId,
            download.DisplayName,
            download.RequestedVersion,
            download.ResolvedVersion,
            download.SourceId,
            download.State,
            download.ChecksumState,
            download.ExpectedSha256,
            download.ActualSha256,
            download.ExtractionState,
            download.ArtifactSource,
            download.CachePath,
            download.InstallPath,
            download.ActivePath,
            download.Summary,
            download.Details,
            download.IsCached,
            download.IsExtracted);
    }

    private static PackageDownloadSummaryDto Map(PackageDownloadManagerSnapshot summary)
    {
        return new PackageDownloadSummaryDto(
            summary.ActiveSelectionCount,
            summary.CachedCount,
            summary.PendingCount,
            summary.MissingCount,
            summary.ErrorCount,
            summary.VerifiedChecksumCount,
            summary.UnverifiedChecksumCount,
            summary.ChecksumMismatchCount,
            summary.ExtractedCount,
            summary.PendingExtractionCount,
            summary.ExtractionErrorCount,
            summary.Summary,
            summary.Details);
    }

    private static RuntimePackageStatusDto Map(RuntimePackageStatusSnapshot runtime)
    {
        return new RuntimePackageStatusDto(
            runtime.PackageId,
            runtime.DisplayName,
            runtime.Family,
            runtime.Kind,
            runtime.State,
            runtime.RequestedVersion,
            runtime.ResolvedVersion,
            runtime.ActiveVersion,
            runtime.DefaultVersion,
            runtime.SourceId,
            runtime.Channel,
            runtime.InstallRootPath,
            runtime.ActivePath,
            runtime.ExecutablePath,
            runtime.Summary,
            runtime.Details,
            runtime.AvailableVersions,
            runtime.InstalledVersions,
            runtime.SupportsSwitching,
            runtime.IsInstalled,
            runtime.IsActive);
    }

    private static RuntimePackageSummaryDto Map(RuntimePackageManagerSnapshot summary)
    {
        return new RuntimePackageSummaryDto(
            summary.RuntimeCount,
            summary.InstalledCount,
            summary.ActiveCount,
            summary.SwitchableCount,
            summary.AttentionCount,
            summary.Summary,
            summary.Details);
    }

    private static ToolPackageStatusDto Map(ToolPackageStatusSnapshot tool)
    {
        return new ToolPackageStatusDto(
            tool.PackageId,
            tool.DisplayName,
            tool.Family,
            tool.Kind,
            tool.State,
            tool.RequestedVersion,
            tool.ResolvedVersion,
            tool.ActiveVersion,
            tool.DefaultVersion,
            tool.SourceId,
            tool.Channel,
            tool.InstallRootPath,
            tool.ActivePath,
            tool.ExecutablePath,
            tool.Summary,
            tool.Details,
            tool.AvailableVersions,
            tool.InstalledVersions,
            tool.ProvidedCommands,
            tool.IsSelected,
            tool.IsInstalled,
            tool.IsActive);
    }

    private static ToolPackageSummaryDto Map(ToolPackageManagerSnapshot summary)
    {
        return new ToolPackageSummaryDto(
            summary.ToolCount,
            summary.SelectedCount,
            summary.InstalledCount,
            summary.ActiveCount,
            summary.AttentionCount,
            summary.Summary,
            summary.Details);
    }

    private static StackProfileStatusDto Map(StackProfileStatusSnapshot profile)
    {
        return new StackProfileStatusDto(
            profile.Key,
            profile.DisplayName,
            profile.Description,
            profile.State,
            profile.ServiceKeys,
            profile.PackageSelections,
            profile.Tags,
            profile.IsActive,
            profile.IsValid,
            profile.Summary,
            profile.Details);
    }

    private static StackProfileSummaryDto Map(StackProfileRegistrySnapshot summary)
    {
        return new StackProfileSummaryDto(
            summary.ProfileCount,
            summary.ValidProfileCount,
            summary.InvalidProfileCount,
            summary.ActiveProfileKey,
            summary.ActiveProfileName,
            summary.Summary,
            summary.Details);
    }

    private static ProjectSummaryDto Map(DiscoveredProject project)
    {
        return new ProjectSummaryDto(
            project.Name,
            project.Path,
            project.Url,
            $"{project.Runtime} / {project.Framework}",
            project.Description,
            project.Tags,
            project.UsesHttps);
    }

    private IReadOnlyList<ValidationResultDto> CreateValidationResults(
        PackageCompatibilityManagerSnapshot packageCompatibility,
        StackProfileRegistrySnapshot stackProfiles)
    {
        var results = new List<ValidationResultDto>();

        if (_nginxConfigValidator.LastResult is { } result)
        {
            results.Add(Map(result));
        }

        results.Add(Map(packageCompatibility));
        results.Add(MapProfileCompatibility(stackProfiles));
        return results;
    }

    private IReadOnlyList<PackageCompatibilityRequirement> CreatePackageCompatibilityRequirements()
    {
        return _registry.Services
            .Select(service => service.Definition)
            .Where(definition => !string.IsNullOrWhiteSpace(definition.RelativeExecutablePath))
            .Select(definition => new PackageCompatibilityRequirement(
                definition.Key,
                definition.DisplayName,
                InferPackageId(definition),
                definition.Version,
                definition.RelativeExecutablePath,
                definition.RelativeStopExecutablePath))
            .ToArray();
    }

    private async Task<IReadOnlyList<DiscoveredProject>> GenerateProjectConfigurationAsync(CancellationToken cancellationToken)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        var sslAwareProjects = _localSslService.ApplySsl(projects);
        await _projectConfigurationWriter.GenerateAsync(sslAwareProjects, cancellationToken);
        return sslAwareProjects;
    }

    private static ValidationResultDto Map(ConfigValidationResult result)
    {
        return new ValidationResultDto(
            result.Key,
            result.DisplayName,
            result.IsValid,
            result.Summary,
            result.Details,
            result.CheckedAt);
    }

    private static ValidationResultDto Map(PackageCompatibilityManagerSnapshot compatibility)
    {
        return new ValidationResultDto(
            "package_service_compatibility",
            "Package/service compatibility",
            compatibility.AttentionCount == 0,
            compatibility.Summary,
            compatibility.Details,
            DateTimeOffset.UtcNow);
    }

    private static ValidationResultDto MapProfileCompatibility(StackProfileRegistrySnapshot stackProfiles)
    {
        return new ValidationResultDto(
            "stack_profile_compatibility",
            "Stack profile compatibility",
            stackProfiles.InvalidProfileCount == 0,
            stackProfiles.Summary,
            stackProfiles.Details,
            DateTimeOffset.UtcNow);
    }

    private static string InferPackageId(Locora.Supervisor.Configuration.ManagedServiceDefinition definition)
    {
        var segments = definition.RelativeExecutablePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length >= 2 && segments[0].Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            return segments[1];
        }

        return string.IsNullOrWhiteSpace(definition.Kind)
            ? definition.Key
            : definition.Kind;
    }

    private static PortDiagnosticDto Map(PortDiagnosticSnapshot diagnostic)
    {
        return new PortDiagnosticDto(
            diagnostic.Key,
            diagnostic.DisplayName,
            diagnostic.Port,
            diagnostic.State,
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction,
            diagnostic.OwnerProcessId,
            diagnostic.OwnerProcessName);
    }

    private static PermissionDiagnosticDto Map(PermissionDiagnosticSnapshot diagnostic)
    {
        return new PermissionDiagnosticDto(
            diagnostic.Key,
            diagnostic.DisplayName,
            diagnostic.State,
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction);
    }

    private static bool ShouldSurfacePortDiagnostic(PortDiagnosticSnapshot diagnostic)
    {
        return diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.State.Equals("Attention", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalPortOwner(ManagedServiceStatus status)
    {
        return status.PortResponsive && status.ProcessId is null;
    }

    private static bool ShouldSurfacePermissionDiagnostic(PermissionDiagnosticSnapshot diagnostic)
    {
        if (diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return diagnostic.State.Equals("Attention", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Key.Equals("hosts_write_path", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DomainCollisionIssue> GetDomainCollisions(IReadOnlyList<DiscoveredProject> projects)
    {
        return projects
            .GroupBy(project => new Uri(project.Url).Host, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new DomainCollisionIssue(group.Key, group.OrderBy(project => project.Name).ToList()))
            .OrderBy(collision => collision.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SslStatusDto Map(LocalSslStatusSnapshot status)
    {
        return new SslStatusDto(
            status.AuthorityName,
            status.CaCertificatePath,
            status.CertificatesRoot,
            status.CaExists,
            status.IsCurrentUserTrusted,
            status.TrustSupported,
            status.ProjectCertificateCount,
            status.HttpsProjectCount,
            status.MissingProjectCertificateCount,
            status.ExpiredProjectCertificateCount,
            status.ExpiringProjectCertificateCount,
            status.Summary,
            status.Details,
            status.LastGeneratedAt,
            status.CaExpiresAt,
            status.CertificateDiagnostics.Select(Map).ToList());
    }

    private static SslCertificateDiagnosticDto Map(LocalSslCertificateDiagnosticSnapshot diagnostic)
    {
        return new SslCertificateDiagnosticDto(
            diagnostic.Key,
            diagnostic.ProjectName,
            diagnostic.Host,
            diagnostic.State,
            diagnostic.Reason,
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction,
            diagnostic.CertificatePath,
            diagnostic.ExpiresAt);
    }

    private sealed record DomainCollisionIssue(string Host, IReadOnlyList<DiscoveredProject> Projects);
}
