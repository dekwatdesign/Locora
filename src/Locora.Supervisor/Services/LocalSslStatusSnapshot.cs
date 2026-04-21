namespace Locora.Supervisor.Services;

public sealed record LocalSslStatusSnapshot(
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
    IReadOnlyList<LocalSslCertificateDiagnosticSnapshot> CertificateDiagnostics);
