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

    public async Task<EnvironmentSnapshot> RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteSupervisorActionAsync(
            () => _supervisorClient.RepairServiceAsync(serviceKey, cancellationToken),
            $"Failed to repair service '{serviceKey}'.",
            cancellationToken);
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

    private EnvironmentSnapshot CreateOfflineSnapshot(string failureReason)
    {
        return new EnvironmentSnapshot(
            EnvironmentRoot: _environmentPaths.AppRoot,
            ActiveProfile: "Bootstrap",
            Services:
            [
                new ServiceDescriptor("nginx", "Nginx", "1.27.x", 80, ServiceState.Unknown, true, "Supervisor not connected yet"),
                new ServiceDescriptor("mariadb", "MariaDB", "11.x", 3306, ServiceState.Unknown, true, "Waiting for first supervisor handshake"),
                new ServiceDescriptor("postgresql", "PostgreSQL", "18.x", 5432, ServiceState.Unknown, false, "Optional database service with local trust auth"),
                new ServiceDescriptor("redis", "Redis", "7.x", 6379, ServiceState.Unknown, false, "Optional cache service"),
                new ServiceDescriptor("mailpit", "Mailpit", "1.x", 1025, ServiceState.Unknown, false, "SMTP catcher with web inbox on http://127.0.0.1:8025/")
            ],
            Projects:
            [
                new ProjectDescriptor(
                    Name: "welcome",
                    Path: Path.Combine(_environmentPaths.ProjectRoot, "welcome"),
                    Url: "http://welcome.locora.test",
                    Runtime: "PHP 8.4 + Nginx",
                    UsesHttps: false)
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
            snapshot.Projects.Select(Map).ToList(),
            snapshot.Issues.Select(Map).ToList(),
            snapshot.ValidationResults.Select(Map).ToList(),
            snapshot.PortDiagnostics.Select(Map).ToList(),
            snapshot.PermissionDiagnostics.Select(Map).ToList(),
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

    private static ProjectDescriptor Map(ProjectSummaryDto project)
    {
        return new ProjectDescriptor(project.Name, project.Path, project.Url, project.Runtime, project.UsesHttps);
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
