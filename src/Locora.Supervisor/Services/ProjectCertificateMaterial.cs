namespace Locora.Supervisor.Services;

public sealed record ProjectCertificateMaterial(
    string Host,
    string CertificatePath,
    string KeyPath,
    string PfxPath,
    DateTimeOffset LastGeneratedAt);
