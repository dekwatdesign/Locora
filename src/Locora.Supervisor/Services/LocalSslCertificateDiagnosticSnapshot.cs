namespace Locora.Supervisor.Services;

public sealed record LocalSslCertificateDiagnosticSnapshot(
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
