namespace Locora.App.Models;

public sealed record SslCertificateDiagnosticCard(
    string Key,
    string ProjectName,
    string Host,
    string StateLabel,
    string Reason,
    string Summary,
    string Details,
    string SuggestedAction,
    string CertificatePath,
    string ExpiresAtLabel);
