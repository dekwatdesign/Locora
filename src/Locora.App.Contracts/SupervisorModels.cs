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
    public const string RepairService = "repair_service";
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
    bool UsesHttps);

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
