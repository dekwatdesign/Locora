namespace Locora.Domain.Entities;

public sealed record SslStatus(
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
    IReadOnlyList<SslCertificateDiagnostic> CertificateDiagnostics);
