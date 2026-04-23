namespace Locora.App.Contracts;

public static class SupervisorCommandNames
{
    public const string GetSnapshot = "get_snapshot";
    public const string StartAll = "start_all";
    public const string StopAll = "stop_all";
    public const string StartService = "start_service";
    public const string StopService = "stop_service";
    public const string ApplyHostsPreview = "apply_hosts_preview";
    public const string RollbackHosts = "rollback_hosts";
    public const string RestartElevated = "restart_elevated";
    public const string ValidateNginxConfig = "validate_nginx_config";
    public const string RepairRuntime = "repair_runtime";
    public const string RepairDomains = "repair_domains";
    public const string RepairService = "repair_service";
    public const string SyncPackageDownloads = "sync_package_downloads";
    public const string ExtractPackageArchives = "extract_package_archives";
    public const string InstallOrUpdatePackages = "install_or_update_packages";
    public const string RemovePackageInstall = "remove_package_install";
    public const string SelectRuntimeVersion = "select_runtime_version";
    public const string SelectStackProfile = "select_stack_profile";
    public const string RepairLocalSsl = "repair_local_ssl";
    public const string GenerateLocalSsl = "generate_local_ssl";
    public const string TrustLocalSslCa = "trust_local_ssl_ca";
    public const string RollbackLocalSslTrust = "rollback_local_ssl_trust";
}

public sealed record SupervisorRequest(Guid CorrelationId, string Command, string? TargetKey = null);

public sealed record SupervisorResponse(
    Guid CorrelationId,
    bool Success,
    string? Message,
    EnvironmentSnapshotDto? Snapshot);

public sealed record EnvironmentSnapshotDto(
    string EnvironmentRoot,
    string ActiveProfile,
    IReadOnlyList<ServiceStatusDto> Services,
    IReadOnlyList<ProjectSummaryDto> Projects,
    IReadOnlyList<HealthIssueDto> Issues,
    IReadOnlyList<ValidationResultDto> ValidationResults,
    IReadOnlyList<PortDiagnosticDto> PortDiagnostics,
    IReadOnlyList<PermissionDiagnosticDto> PermissionDiagnostics,
    IReadOnlyList<PackageSourceStatusDto> PackageSources,
    PackageRegistrySummaryDto PackageRegistry,
    IReadOnlyList<PackageDownloadStatusDto> PackageDownloads,
    PackageDownloadSummaryDto PackageDownloadsSummary,
    IReadOnlyList<RuntimePackageStatusDto> RuntimePackages,
    RuntimePackageSummaryDto RuntimePackageSummary,
    IReadOnlyList<ToolPackageStatusDto> ToolPackages,
    ToolPackageSummaryDto ToolPackageSummary,
    IReadOnlyList<StackProfileStatusDto> StackProfiles,
    StackProfileSummaryDto StackProfileSummary,
    SslStatusDto SslStatus,
    DateTimeOffset GeneratedAt,
    bool IsSupervisorElevated,
    int SupervisorProcessId);

public sealed record ServiceStatusDto(
    string Key,
    string DisplayName,
    string Version,
    int? Port,
    string State,
    bool AutoStart,
    string? Note);

public sealed record ProjectSummaryDto(
    string Name,
    string Path,
    string Url,
    string Runtime,
    string Description,
    IReadOnlyList<string> Tags,
    bool UsesHttps);

public sealed record StackProfileStatusDto(
    string Key,
    string DisplayName,
    string Description,
    string State,
    IReadOnlyList<string> ServiceKeys,
    IReadOnlyDictionary<string, string> PackageSelections,
    IReadOnlyList<string> Tags,
    bool IsActive,
    bool IsValid,
    string Summary,
    string Details);

public sealed record StackProfileSummaryDto(
    int ProfileCount,
    int ValidProfileCount,
    int InvalidProfileCount,
    string ActiveProfileKey,
    string ActiveProfileName,
    string Summary,
    string Details);

public sealed record HealthIssueDto(
    string Severity,
    string Title,
    string Description,
    string SuggestedAction);

public sealed record ValidationResultDto(
    string Key,
    string DisplayName,
    bool IsValid,
    string Summary,
    string Details,
    DateTimeOffset CheckedAt);

public sealed record PortDiagnosticDto(
    string Key,
    string DisplayName,
    int Port,
    string State,
    string Summary,
    string Details,
    string SuggestedAction,
    int? OwnerProcessId,
    string? OwnerProcessName);

public sealed record PermissionDiagnosticDto(
    string Key,
    string DisplayName,
    string State,
    string Summary,
    string Details,
    string SuggestedAction);

public sealed record PackageSourceStatusDto(
    string Id,
    string DisplayName,
    string Kind,
    string State,
    string Channel,
    int Priority,
    string ManifestPath,
    int PackageCount,
    int VersionCount,
    string Summary,
    string Details,
    bool IsEnabled);

public sealed record PackageRegistrySummaryDto(
    int EnabledSourceCount,
    int ReadySourceCount,
    int ErrorSourceCount,
    int PackageCount,
    int VersionCount,
    string Summary,
    string Details);

public sealed record PackageDownloadStatusDto(
    string PackageId,
    string DisplayName,
    string RequestedVersion,
    string ResolvedVersion,
    string SourceId,
    string State,
    string ChecksumState,
    string? ExpectedSha256,
    string? ActualSha256,
    string ExtractionState,
    string ArtifactSource,
    string CachePath,
    string InstallPath,
    string ActivePath,
    string Summary,
    string Details,
    bool IsCached,
    bool IsExtracted);

public sealed record PackageDownloadSummaryDto(
    int ActiveSelectionCount,
    int CachedCount,
    int PendingCount,
    int MissingCount,
    int ErrorCount,
    int VerifiedChecksumCount,
    int UnverifiedChecksumCount,
    int ChecksumMismatchCount,
    int ExtractedCount,
    int PendingExtractionCount,
    int ExtractionErrorCount,
    string Summary,
    string Details);

public sealed record RuntimePackageStatusDto(
    string PackageId,
    string DisplayName,
    string Family,
    string Kind,
    string State,
    string RequestedVersion,
    string ResolvedVersion,
    string ActiveVersion,
    string DefaultVersion,
    string SourceId,
    string Channel,
    string InstallRootPath,
    string ActivePath,
    string ExecutablePath,
    string Summary,
    string Details,
    IReadOnlyList<string> AvailableVersions,
    IReadOnlyList<string> InstalledVersions,
    bool SupportsSwitching,
    bool IsInstalled,
    bool IsActive);

public sealed record RuntimePackageSummaryDto(
    int RuntimeCount,
    int InstalledCount,
    int ActiveCount,
    int SwitchableCount,
    int AttentionCount,
    string Summary,
    string Details);

public sealed record ToolPackageStatusDto(
    string PackageId,
    string DisplayName,
    string Family,
    string Kind,
    string State,
    string RequestedVersion,
    string ResolvedVersion,
    string ActiveVersion,
    string DefaultVersion,
    string SourceId,
    string Channel,
    string InstallRootPath,
    string ActivePath,
    string ExecutablePath,
    string Summary,
    string Details,
    IReadOnlyList<string> AvailableVersions,
    IReadOnlyList<string> InstalledVersions,
    IReadOnlyList<string> ProvidedCommands,
    bool IsSelected,
    bool IsInstalled,
    bool IsActive);

public sealed record ToolPackageSummaryDto(
    int ToolCount,
    int SelectedCount,
    int InstalledCount,
    int ActiveCount,
    int AttentionCount,
    string Summary,
    string Details);

public sealed record SslStatusDto(
    string AuthorityName,
    string CaCertificatePath,
    string CertificatesRoot,
    bool CaExists,
    bool IsCurrentUserTrusted,
    bool TrustSupported,
    int ProjectCertificateCount,
    int HttpsProjectCount,
    int MissingProjectCertificateCount,
    int ExpiredProjectCertificateCount,
    int ExpiringProjectCertificateCount,
    string Summary,
    string Details,
    DateTimeOffset? LastGeneratedAt,
    DateTimeOffset? CaExpiresAt,
    IReadOnlyList<SslCertificateDiagnosticDto> CertificateDiagnostics);

public sealed record SslCertificateDiagnosticDto(
    string Key,
    string ProjectName,
    string Host,
    string State,
    string Reason,
    string Summary,
    string Details,
    string SuggestedAction,
    string CertificatePath,
    DateTimeOffset? ExpiresAt);
