using Locora.App.Contracts;
using Locora.Application.Abstractions;
using Locora.Domain.Entities;
using Locora.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Locora.Application.Services;

public sealed class WorkbenchService : IWorkbenchService
{
    private readonly ISupervisorClient _supervisorClient;
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly ILogger<WorkbenchService> _logger;

    public WorkbenchService(
        ISupervisorClient supervisorClient,
        IEnvironmentPaths environmentPaths,
        ILogger<WorkbenchService> logger)
    {
        _supervisorClient = supervisorClient;
        _environmentPaths = environmentPaths;
        _logger = logger;
    }

    public async Task<EnvironmentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _supervisorClient.GetEnvironmentSnapshotAsync(cancellationToken);
            return Map(snapshot, isSupervisorReachable: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supervisor was unavailable while fetching the current environment snapshot.");
            return CreateOfflineSnapshot(exception.Message);
        }
    }

    public async Task<EnvironmentSnapshot> StartAllAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StartAllAsync(cancellationToken),
            "Failed to start all services through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> StopAllAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StopAllAsync(cancellationToken),
            "Failed to stop all services through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StartServiceAsync(serviceKey, cancellationToken),
            $"Failed to start service '{serviceKey}' through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StopServiceAsync(serviceKey, cancellationToken),
            $"Failed to stop service '{serviceKey}' through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> StartServicePresetAsync(string presetKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StartServicePresetAsync(presetKey, cancellationToken),
            $"Failed to start service preset '{presetKey}' through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> StopServicePresetAsync(string presetKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.StopServicePresetAsync(presetKey, cancellationToken),
            $"Failed to stop service preset '{presetKey}' through the supervisor.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> ApplyHostsPreviewAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.ApplyHostsPreviewAsync(cancellationToken),
            "Failed to apply the generated hosts preview.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RollbackHostsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RollbackHostsAsync(cancellationToken),
            "Failed to roll back the hosts file.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RestartSupervisorElevatedAsync(cancellationToken),
            "Failed to request elevated supervisor restart.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> ValidateNginxConfigAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.ValidateNginxConfigAsync(cancellationToken),
            "Failed to validate the generated Nginx config.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RepairRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RepairRuntimeAsync(cancellationToken),
            "Failed to repair common runtime configuration issues.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RepairDomainsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RepairDomainsAsync(cancellationToken),
            "Failed to regenerate hosts and vhost artifacts.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RepairServiceAsync(serviceKey, cancellationToken),
            $"Failed to repair service '{serviceKey}'.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> SyncPackageDownloadsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.SyncPackageDownloadsAsync(cancellationToken),
            "Failed to sync package downloads into the cache.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> ExtractPackageArchivesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.ExtractPackageArchivesAsync(cancellationToken),
            "Failed to extract cached package archives into the runtime bin roots.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.InstallOrUpdatePackagesAsync(cancellationToken),
            "Failed to install or update the active package selections.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RemovePackageInstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RemovePackageInstallAsync(packageId, cancellationToken),
            $"Failed to remove the installed package '{packageId}'.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> SelectRuntimeVersionAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.SelectRuntimeVersionAsync(BuildRuntimeSelectionKey(packageId, version), cancellationToken),
            $"Failed to select runtime package '{packageId}' version '{version}'.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> SelectStackProfileAsync(string profileKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.SelectStackProfileAsync(profileKey, cancellationToken),
            $"Failed to select stack profile '{profileKey}'.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> SaveActiveEnvironmentAsync(string profileName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supervisorClient.SaveActiveEnvironmentAsync(profileName, cancellationToken);
        }
        catch (Exception exception)
        {
            const string failureMessage = "Failed to save active environment as a stack profile.";
            _logger.LogWarning(exception, "{Message}", failureMessage);
            throw new InvalidOperationException(failureMessage, exception);
        }

        return await GetSnapshotAsync(cancellationToken);
    }

    public async Task<EnvironmentSnapshot> ExportStackProfilesAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supervisorClient.ExportStackProfilesAsync(targetPath, cancellationToken);
        }
        catch (Exception exception)
        {
            const string failureMessage = "Failed to export stack profiles.";
            _logger.LogWarning(exception, "{Message}", failureMessage);
            throw new InvalidOperationException(failureMessage, exception);
        }

        return await GetSnapshotAsync(cancellationToken);
    }

    public async Task<EnvironmentSnapshot> ImportStackProfilesAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supervisorClient.ImportStackProfilesAsync(sourcePath, cancellationToken);
        }
        catch (Exception exception)
        {
            const string failureMessage = "Failed to import stack profiles.";
            _logger.LogWarning(exception, "{Message}", failureMessage);
            throw new InvalidOperationException(failureMessage, exception);
        }

        return await GetSnapshotAsync(cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RepairLocalSslAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RepairLocalSslAsync(cancellationToken),
            "Failed to repair local SSL certificate material.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> GenerateLocalSslAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.GenerateLocalSslAsync(cancellationToken),
            "Failed to generate local SSL certificates.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> TrustLocalSslCaAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.TrustLocalSslCaAsync(cancellationToken),
            "Failed to trust the local SSL certificate authority.",
            cancellationToken);
    }

    public async Task<EnvironmentSnapshot> RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RollbackLocalSslTrustAsync(cancellationToken),
            "Failed to remove Locora CA trust from the current user certificate store.",
            cancellationToken);
    }

    private async Task<EnvironmentSnapshot> ExecuteSupervisorActionAsync(
        Func<Task> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{Message}", failureMessage);
        }

        return await GetSnapshotAsync(cancellationToken);
    }

    private static string BuildRuntimeSelectionKey(string packageId, string version)
    {
        return $"{packageId}|{version}";
    }

    private EnvironmentSnapshot CreateOfflineSnapshot(string failureReason)
    {
        return new EnvironmentSnapshot(
            EnvironmentRoot: _environmentPaths.AppRoot,
            ActiveProfile: "Bootstrap",
            Services:
            [
                new ServiceDescriptor("nginx", "Nginx", "1.27.x", 80, ServiceState.Unknown, true, "Supervisor not connected yet"),
                new ServiceDescriptor("apache", "Apache", "2.4.x", 8080, ServiceState.Unknown, false, "Optional alternate web server"),
                new ServiceDescriptor("mariadb", "MariaDB", "11.x", 3306, ServiceState.Unknown, true, "Waiting for first supervisor handshake"),
                new ServiceDescriptor("postgresql", "PostgreSQL", "18.x", 5432, ServiceState.Unknown, false, "Optional database service with local trust auth"),
                new ServiceDescriptor("redis", "Redis", "7.x", 6379, ServiceState.Unknown, false, "Optional cache service"),
                new ServiceDescriptor("memcached", "Memcached", "1.6.x", 11211, ServiceState.Unknown, false, "Optional in-memory cache service"),
                new ServiceDescriptor("mailpit", "Mailpit", "1.x", 1025, ServiceState.Unknown, false, "SMTP catcher with web inbox on http://127.0.0.1:8025/")
            ],
            ServicePresets:
            [
                new ServicePresetStatus(
                    Key: "web-db-mail",
                    DisplayName: "Web + DB + Mail",
                    Description: "Default offline preset for Nginx, MariaDB, and Mailpit.",
                    ServiceKeys: ["nginx", "mariadb", "mailpit"],
                    Tags: ["web", "database", "mail"],
                    IsValid: false,
                    State: "Unavailable",
                    ServicesLabel: "Nginx, MariaDB, Mailpit",
                    Summary: "Service presets unavailable",
                    Details: "The supervisor is offline, so service presets could not be loaded yet.")
            ],
            Projects:
            [
                new ProjectDescriptor(
                    Name: "welcome",
                    Path: Path.Combine(_environmentPaths.ProjectRoot, "welcome"),
                    Url: "http://welcome.locora.test",
                    Runtime: "PHP 8.4 + Nginx",
                    Description: "Welcome project generated by Locora.",
                    Tags: ["sample", "php"],
                    UsesHttps: false,
                    OverrideSummary: "Overrides: none")
            ],
            Issues:
            [
                new HealthIssue(
                    HealthIssueSeverity.Warning,
                    "Supervisor offline",
                    $"The app could not reach the local supervisor process. Reason: {failureReason}",
                    "Launch Locora.Supervisor first, then refresh the dashboard.")
            ],
            ValidationResults: [],
            PortDiagnostics:
            [
                new PortDiagnostic(
                    "supervisor_connection_ports",
                    "Port diagnostics",
                    0,
                    PortDiagnosticState.Attention,
                    "Port diagnostics are unavailable while the supervisor is offline.",
                    failureReason,
                    "Launch Locora.Supervisor first, then refresh diagnostics.",
                    null,
                    null)
            ],
            PermissionDiagnostics:
            [
                new PermissionDiagnostic(
                    "supervisor_connection",
                    "Supervisor permission diagnostics",
                    PermissionDiagnosticState.Attention,
                    "Permission and elevation diagnostics are unavailable while the supervisor is offline.",
                    failureReason,
                    "Launch Locora.Supervisor first, then refresh the dashboard.")
            ],
            PackageSources: [],
            PackageRegistry: new PackageRegistrySummary(
                EnabledSourceCount: 0,
                ReadySourceCount: 0,
                ErrorSourceCount: 0,
                PackageCount: 0,
                VersionCount: 0,
                Summary: "Package source registry unavailable",
                Details: "The supervisor is offline, so package source status could not be loaded yet."),
            PackageDownloads: [],
            PackageDownloadsSummary: new PackageDownloadSummary(
                ActiveSelectionCount: 0,
                CachedCount: 0,
                PendingCount: 0,
                MissingCount: 0,
                ErrorCount: 0,
                VerifiedChecksumCount: 0,
                UnverifiedChecksumCount: 0,
                ChecksumMismatchCount: 0,
                ExtractedCount: 0,
                PendingExtractionCount: 0,
                ExtractionErrorCount: 0,
                Summary: "Package download manager unavailable",
                Details: "The supervisor is offline, so package download planning and cache status could not be loaded yet."),
            RuntimePackages: [],
            RuntimePackageSummary: new RuntimePackageSummary(
                RuntimeCount: 0,
                InstalledCount: 0,
                ActiveCount: 0,
                SwitchableCount: 0,
                AttentionCount: 0,
                Summary: "Runtime version inventory unavailable",
                Details: "The supervisor is offline, so runtime package versions could not be loaded yet."),
            ToolPackages: [],
            ToolPackageSummary: new ToolPackageSummary(
                ToolCount: 0,
                SelectedCount: 0,
                InstalledCount: 0,
                ActiveCount: 0,
                AttentionCount: 0,
                Summary: "Tool package inventory unavailable",
                Details: "The supervisor is offline, so tool package inventory could not be loaded yet."),
            StackProfiles: [],
            StackProfileSummary: new StackProfileSummary(
                ProfileCount: 0,
                ValidProfileCount: 0,
                InvalidProfileCount: 0,
                ActiveProfileKey: "bootstrap",
                ActiveProfileName: "Bootstrap",
                Summary: "Stack profiles unavailable",
                Details: "The supervisor is offline, so stack profiles could not be loaded yet."),
            SslStatus: new SslStatus(
                AuthorityName: "Locora Local Development CA",
                CaCertificatePath: Path.Combine(_environmentPaths.ConfigRoot, "ssl", "ca", "locora-root-ca.cer"),
                CertificatesRoot: Path.Combine(_environmentPaths.ConfigRoot, "ssl", "certs"),
                CaExists: false,
                IsCurrentUserTrusted: false,
                TrustSupported: OperatingSystem.IsWindows(),
                ProjectCertificateCount: 0,
                HttpsProjectCount: 0,
                MissingProjectCertificateCount: 0,
                ExpiredProjectCertificateCount: 0,
                ExpiringProjectCertificateCount: 0,
                Summary: "Local SSL state unavailable",
                Details: "The supervisor is offline, so SSL generation and trust state could not be checked yet.",
                LastGeneratedAt: null,
                CaExpiresAt: null,
                CertificateDiagnostics: []),
            GeneratedAt: DateTimeOffset.UtcNow,
            IsSupervisorReachable: false,
            IsSupervisorElevated: false,
            SupervisorProcessId: null);
    }

    private static EnvironmentSnapshot Map(EnvironmentSnapshotDto snapshot, bool isSupervisorReachable)
    {
        return new EnvironmentSnapshot(
            snapshot.EnvironmentRoot,
            snapshot.ActiveProfile,
            snapshot.Services.Select(Map).ToList(),
            snapshot.ServicePresets.Select(Map).ToList(),
            snapshot.Projects.Select(Map).ToList(),
            snapshot.Issues.Select(Map).ToList(),
            snapshot.ValidationResults.Select(Map).ToList(),
            snapshot.PortDiagnostics.Select(Map).ToList(),
            snapshot.PermissionDiagnostics.Select(Map).ToList(),
            snapshot.PackageSources.Select(Map).ToList(),
            Map(snapshot.PackageRegistry),
            snapshot.PackageDownloads.Select(Map).ToList(),
            Map(snapshot.PackageDownloadsSummary),
            snapshot.RuntimePackages.Select(Map).ToList(),
            Map(snapshot.RuntimePackageSummary),
            snapshot.ToolPackages.Select(Map).ToList(),
            Map(snapshot.ToolPackageSummary),
            snapshot.StackProfiles.Select(Map).ToList(),
            Map(snapshot.StackProfileSummary),
            Map(snapshot.SslStatus),
            snapshot.GeneratedAt,
            isSupervisorReachable,
            snapshot.IsSupervisorElevated,
            snapshot.SupervisorProcessId);
    }

    private static ServiceDescriptor Map(ServiceStatusDto service)
    {
        return new ServiceDescriptor(
            service.Key,
            service.DisplayName,
            service.Version,
            service.Port,
            service.State.ToLowerInvariant() switch
            {
                "starting" => ServiceState.Starting,
                "running" => ServiceState.Running,
                "stopped" => ServiceState.Stopped,
                "error" => ServiceState.Error,
                _ => ServiceState.Unknown
            },
            service.AutoStart,
            service.Note);
    }

    private static ServicePresetStatus Map(ServicePresetStatusDto preset)
    {
        return new ServicePresetStatus(
            preset.Key,
            preset.DisplayName,
            preset.Description,
            preset.ServiceKeys,
            preset.Tags,
            preset.IsValid,
            preset.State,
            preset.ServicesLabel,
            preset.Summary,
            preset.Details);
    }

    private static ProjectDescriptor Map(ProjectSummaryDto project)
    {
        return new ProjectDescriptor(
            project.Name,
            project.Path,
            project.Url,
            project.Runtime,
            project.Description ?? string.Empty,
            project.Tags ?? Array.Empty<string>(),
            project.UsesHttps,
            project.OverrideSummary);
    }

    private static HealthIssue Map(HealthIssueDto issue)
    {
        return new HealthIssue(
            issue.Severity.ToLowerInvariant() switch
            {
                "warning" => HealthIssueSeverity.Warning,
                "error" => HealthIssueSeverity.Error,
                _ => HealthIssueSeverity.Info
            },
            issue.Title,
            issue.Description,
            issue.SuggestedAction);
    }

    private static ValidationResult Map(ValidationResultDto validation)
    {
        return new ValidationResult(
            validation.Key,
            validation.DisplayName,
            validation.IsValid,
            validation.Summary,
            validation.Details,
            validation.CheckedAt);
    }

    private static PortDiagnostic Map(PortDiagnosticDto diagnostic)
    {
        return new PortDiagnostic(
            diagnostic.Key,
            diagnostic.DisplayName,
            diagnostic.Port,
            diagnostic.State.ToLowerInvariant() switch
            {
                "attention" => PortDiagnosticState.Attention,
                "blocked" => PortDiagnosticState.Blocked,
                _ => PortDiagnosticState.Ready
            },
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction,
            diagnostic.OwnerProcessId,
            diagnostic.OwnerProcessName);
    }

    private static PermissionDiagnostic Map(PermissionDiagnosticDto diagnostic)
    {
        return new PermissionDiagnostic(
            diagnostic.Key,
            diagnostic.DisplayName,
            diagnostic.State.ToLowerInvariant() switch
            {
                "attention" => PermissionDiagnosticState.Attention,
                "blocked" => PermissionDiagnosticState.Blocked,
                _ => PermissionDiagnosticState.Ready
            },
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction);
    }

    private static PackageSourceStatus Map(PackageSourceStatusDto source)
    {
        return new PackageSourceStatus(
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

    private static PackageRegistrySummary Map(PackageRegistrySummaryDto registry)
    {
        return new PackageRegistrySummary(
            registry.EnabledSourceCount,
            registry.ReadySourceCount,
            registry.ErrorSourceCount,
            registry.PackageCount,
            registry.VersionCount,
            registry.Summary,
            registry.Details);
    }

    private static PackageDownloadStatus Map(PackageDownloadStatusDto download)
    {
        return new PackageDownloadStatus(
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

    private static PackageDownloadSummary Map(PackageDownloadSummaryDto summary)
    {
        return new PackageDownloadSummary(
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

    private static RuntimePackageStatus Map(RuntimePackageStatusDto runtime)
    {
        return new RuntimePackageStatus(
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

    private static RuntimePackageSummary Map(RuntimePackageSummaryDto summary)
    {
        return new RuntimePackageSummary(
            summary.RuntimeCount,
            summary.InstalledCount,
            summary.ActiveCount,
            summary.SwitchableCount,
            summary.AttentionCount,
            summary.Summary,
            summary.Details);
    }

    private static ToolPackageStatus Map(ToolPackageStatusDto tool)
    {
        return new ToolPackageStatus(
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

    private static ToolPackageSummary Map(ToolPackageSummaryDto summary)
    {
        return new ToolPackageSummary(
            summary.ToolCount,
            summary.SelectedCount,
            summary.InstalledCount,
            summary.ActiveCount,
            summary.AttentionCount,
            summary.Summary,
            summary.Details);
    }

    private static StackProfileStatus Map(StackProfileStatusDto profile)
    {
        return new StackProfileStatus(
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

    private static StackProfileSummary Map(StackProfileSummaryDto summary)
    {
        return new StackProfileSummary(
            summary.ProfileCount,
            summary.ValidProfileCount,
            summary.InvalidProfileCount,
            summary.ActiveProfileKey,
            summary.ActiveProfileName,
            summary.Summary,
            summary.Details);
    }

    private static SslStatus Map(SslStatusDto sslStatus)
    {
        return new SslStatus(
            sslStatus.AuthorityName,
            sslStatus.CaCertificatePath,
            sslStatus.CertificatesRoot,
            sslStatus.CaExists,
            sslStatus.IsCurrentUserTrusted,
            sslStatus.TrustSupported,
            sslStatus.ProjectCertificateCount,
            sslStatus.HttpsProjectCount,
            sslStatus.MissingProjectCertificateCount,
            sslStatus.ExpiredProjectCertificateCount,
            sslStatus.ExpiringProjectCertificateCount,
            sslStatus.Summary,
            sslStatus.Details,
            sslStatus.LastGeneratedAt,
            sslStatus.CaExpiresAt,
            sslStatus.CertificateDiagnostics.Select(Map).ToList());
    }

    private static SslCertificateDiagnostic Map(SslCertificateDiagnosticDto diagnostic)
    {
        return new SslCertificateDiagnostic(
            diagnostic.Key,
            diagnostic.ProjectName,
            diagnostic.Host,
            diagnostic.State.ToLowerInvariant() switch
            {
                "attention" => SslCertificateDiagnosticState.Attention,
                "blocked" => SslCertificateDiagnosticState.Blocked,
                _ => SslCertificateDiagnosticState.Ready
            },
            diagnostic.Reason,
            diagnostic.Summary,
            diagnostic.Details,
            diagnostic.SuggestedAction,
            diagnostic.CertificatePath,
            diagnostic.ExpiresAt);
    }
}
