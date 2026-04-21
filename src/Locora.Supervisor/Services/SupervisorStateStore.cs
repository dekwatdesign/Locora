using Locora.App.Contracts;

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
        _runtimeRepairService = runtimeRepairService;
        _appRoot = environmentPaths.AppRoot;
    }

    public async Task<EnvironmentSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var statuses = await _registry.GetStatusesAsync(cancellationToken);
        var projects = await GenerateProjectConfigurationAsync(cancellationToken);
        var validationResults = CreateValidationResults();
        var portDiagnostics = _portDiagnosticsService.GetDiagnostics(statuses);
        var permissionDiagnostics = _permissionDiagnosticsService.GetDiagnostics();
        var sslStatus = await _localSslService.GetStatusAsync(projects, cancellationToken);

        return new EnvironmentSnapshotDto(
            _appRoot,
            "Full Stack",
            statuses.Select(Map).ToList(),
            projects.Select(Map).ToList(),
            CreateIssues(statuses, projects, validationResults, portDiagnostics, permissionDiagnostics, sslStatus),
            validationResults,
            portDiagnostics.Select(Map).ToList(),
            permissionDiagnostics.Select(Map).ToList(),
            Map(sslStatus),
            DateTimeOffset.UtcNow,
            _privilegeService.IsElevated(),
            Environment.ProcessId);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        await GenerateProjectConfigurationAsync(cancellationToken);
        await _registry.StartAllAsync(cancellationToken);
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

    public async Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await _runtimeRepairService.RepairServiceAsync(serviceKey, cancellationToken);
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
                    "Add service definitions for Nginx, MariaDB, PostgreSQL, Redis, Mailpit, and related tools."));
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
                    "Use Repair Runtime Configs from Services or Domains & Hosts, then review the validation output before restarting."));
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
                    "Next, install managed runtime packages or wire package download/install automation."));
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

    private static ProjectSummaryDto Map(DiscoveredProject project)
    {
        return new ProjectSummaryDto(
            project.Name,
            project.Path,
            project.Url,
            $"{project.Runtime} / {project.Framework}",
            project.UsesHttps);
    }

    private IReadOnlyList<ValidationResultDto> CreateValidationResults()
    {
        return _nginxConfigValidator.LastResult is { } result
            ? [Map(result)]
            : [];
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
}
