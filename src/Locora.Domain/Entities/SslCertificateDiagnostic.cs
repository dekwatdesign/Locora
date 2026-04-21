using Locora.Domain.Enums;

namespace Locora.Domain.Entities;

public sealed record SslCertificateDiagnostic(
    string Key,
    string ProjectName,
    string Host,
    SslCertificateDiagnosticState State,
    string Reason,
    string Summary,
    string Details,
    string SuggestedAction,
    string CertificatePath,
    DateTimeOffset? ExpiresAt);
