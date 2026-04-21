namespace Locora.Domain.Entities;

public sealed record EnvironmentSnapshot(
    string EnvironmentRoot,
    string ActiveProfile,
    IReadOnlyList<ServiceDescriptor> Services,
    IReadOnlyList<ProjectDescriptor> Projects,
    IReadOnlyList<HealthIssue> Issues,
    IReadOnlyList<ValidationResult> ValidationResults,
    IReadOnlyList<PortDiagnostic> PortDiagnostics,
    IReadOnlyList<PermissionDiagnostic> PermissionDiagnostics,
    SslStatus SslStatus,
    DateTimeOffset GeneratedAt,
    bool IsSupervisorReachable,
    bool IsSupervisorElevated,
    int? SupervisorProcessId);
