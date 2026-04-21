using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Locora.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class LocalSslService
{
    private const string AuthorityName = "Locora Local Development CA";
    private const string CaPassword = "locora-local-ca";
    private static readonly TimeSpan CertificateRenewalWindow = TimeSpan.FromDays(30);
    private readonly IEnvironmentPaths _paths;
    private readonly ILogger<LocalSslService> _logger;

    public LocalSslService(
        IEnvironmentPaths paths,
        ILogger<LocalSslService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<LocalSslStatusSnapshot> GenerateAsync(
        IReadOnlyList<DiscoveredProject> projects,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ca = EnsureCertificateAuthority();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureProjectCertificate(project, ca);
        }

        _logger.LogInformation("Generated local SSL certificate material for {Count} projects.", projects.Count);
        return await GetStatusAsync(projects, cancellationToken);
    }

    public async Task<LocalSslStatusSnapshot> RepairAsync(
        IReadOnlyList<DiscoveredProject> projects,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var caWasRepaired = !IsCertificateAuthorityUsable();
        using var ca = EnsureCertificateAuthority();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostic = CreateProjectCertificateDiagnostic(project, ca, GetCaLastGeneratedAt());
            if (caWasRepaired || IsRepairable(diagnostic))
            {
                EnsureProjectCertificate(project, ca);
            }
        }

        _logger.LogInformation("Repaired local SSL certificate material for {Count} projects.", projects.Count);
        return await GetStatusAsync(projects, cancellationToken);
    }

    public Task<LocalSslStatusSnapshot> TrustCurrentUserAsync(
        IReadOnlyList<DiscoveredProject> projects,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return GetStatusAsync(projects, cancellationToken);
        }

        if (!IsCertificateAuthorityUsable())
        {
            throw new InvalidOperationException("Repair or generate the local SSL certificate authority before trusting it.");
        }

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        using var ca = LoadCertificateAuthorityPublicCertificate();

        if (!FindTrustedCertificates(store).Any())
        {
            store.Add(ca);
            _logger.LogInformation("Trusted local SSL certificate authority in the CurrentUser root store.");
        }

        return GetStatusAsync(projects, cancellationToken);
    }

    public Task<LocalSslStatusSnapshot> RemoveCurrentUserTrustAsync(
        IReadOnlyList<DiscoveredProject> projects,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return GetStatusAsync(projects, cancellationToken);
        }

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var certificates = FindTrustedCertificates(store).ToList();
        foreach (var certificate in certificates)
        {
            store.Remove(certificate);
        }

        if (certificates.Count > 0)
        {
            _logger.LogInformation("Removed {Count} Locora CA certificate(s) from the CurrentUser root store.", certificates.Count);
        }

        return GetStatusAsync(projects, cancellationToken);
    }

    public IReadOnlyList<DiscoveredProject> ApplySsl(IReadOnlyList<DiscoveredProject> projects)
    {
        if (!TryLoadUsableCertificateAuthority(out var ca, out _))
        {
            return projects;
        }

        using (ca)
        {
            var caLastGeneratedAt = GetCaLastGeneratedAt();
            return projects
                .Select(project =>
                {
                    var diagnostic = CreateProjectCertificateDiagnostic(project, ca, caLastGeneratedAt);
                    if (diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
                    {
                        return project;
                    }

                    var host = new Uri(project.Url).Host;
                    return project with
                    {
                        Url = $"https://{host}",
                        UsesHttps = true
                    };
                })
                .ToList();
        }
    }

    public ProjectCertificateMaterial? GetProjectCertificateMaterial(DiscoveredProject project)
    {
        var host = new Uri(project.Url).Host;
        return GetProjectCertificateMaterial(project.Slug, host);
    }

    public Task<LocalSslStatusSnapshot> GetStatusAsync(
        IReadOnlyList<DiscoveredProject> projects,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var caCertificatePath = GetCaCertificatePath();
        var certificatesRoot = GetCertificatesRoot();
        Directory.CreateDirectory(certificatesRoot);

        var caExists = File.Exists(caCertificatePath);
        var caExpiresAt = GetCaExpiresAt();
        using var ca = TryLoadUsableCertificateAuthority(out var loadedCa, out _) ? loadedCa : null;
        var caUsable = ca is not null;
        var caLastGeneratedAt = GetCaLastGeneratedAt();
        var certificateDiagnostics = projects
            .Select(project => CreateProjectCertificateDiagnostic(project, ca, caLastGeneratedAt))
            .ToList();
        var sslAwareProjects = ApplySsl(projects);
        var projectCertificateCount = certificateDiagnostics.Count(diagnostic => !diagnostic.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase));
        var httpsProjectCount = sslAwareProjects.Count(project => project.UsesHttps);
        var missingProjectCertificateCount = certificateDiagnostics.Count(diagnostic =>
            diagnostic.Reason.Equals("Missing", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Reason.Equals("MissingAuthority", StringComparison.OrdinalIgnoreCase));
        var expiredProjectCertificateCount = certificateDiagnostics.Count(diagnostic =>
            diagnostic.Reason.Equals("Expired", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Reason.Equals("Invalid", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Reason.Equals("StaleAuthority", StringComparison.OrdinalIgnoreCase));
        var expiringProjectCertificateCount = certificateDiagnostics.Count(diagnostic => diagnostic.Reason.Equals("ExpiringSoon", StringComparison.OrdinalIgnoreCase));
        var trustSupported = OperatingSystem.IsWindows();
        var trusted = caExists && trustSupported && IsCurrentUserTrusted();
        var projectMaterials = projects
            .Select(GetProjectCertificateMaterial)
            .Where(material => material is not null)
            .Cast<ProjectCertificateMaterial>()
            .ToList();
        var lastGeneratedAt = GetLastGeneratedAt(caExists ? caCertificatePath : null, projectMaterials);
        var summary = CreateSummary(caExists, caUsable, trusted, trustSupported, projectCertificateCount, missingProjectCertificateCount, expiredProjectCertificateCount, expiringProjectCertificateCount);
        var details = CreateDetails(caExists, caUsable, trusted, trustSupported, projectCertificateCount, httpsProjectCount, missingProjectCertificateCount, expiredProjectCertificateCount, expiringProjectCertificateCount);

        return Task.FromResult(
            new LocalSslStatusSnapshot(
                AuthorityName,
                caCertificatePath,
                certificatesRoot,
                caExists,
                trusted,
                trustSupported,
                projectCertificateCount,
                httpsProjectCount,
                missingProjectCertificateCount,
                expiredProjectCertificateCount,
                expiringProjectCertificateCount,
                summary,
                details,
                lastGeneratedAt,
                caExpiresAt,
                certificateDiagnostics));
    }

    private LocalSslCertificateDiagnosticSnapshot CreateProjectCertificateDiagnostic(
        DiscoveredProject project,
        X509Certificate2? certificateAuthority,
        DateTimeOffset? caLastGeneratedAt)
    {
        var host = new Uri(project.Url).Host;
        var materialRoot = Path.Combine(GetCertificatesRoot(), project.Slug);
        var certificatePath = Path.Combine(materialRoot, $"{host}.crt.pem");
        var keyPath = Path.Combine(materialRoot, $"{host}.key.pem");
        var pfxPath = Path.Combine(materialRoot, $"{host}.pfx");

        if (certificateAuthority is null)
        {
            return CreateCertificateDiagnostic(
                project,
                host,
                certificatePath,
                "Blocked",
                "MissingAuthority",
                "Local SSL certificate authority is missing or expired.",
                "Project certificates cannot be validated until the Locora CA is repaired.",
                "Run Repair Local SSL to regenerate the Locora CA and project certificates.",
                null);
        }

        if (!File.Exists(certificatePath) || !File.Exists(keyPath) || !File.Exists(pfxPath))
        {
            return CreateCertificateDiagnostic(
                project,
                host,
                certificatePath,
                "Blocked",
                "Missing",
                $"Certificate material is missing for {host}.",
                $"Expected certificate, key, and PFX files under {materialRoot}.",
                "Run Repair Local SSL to recreate the missing project certificate material.",
                null);
        }

        try
        {
            using var certificate = new X509Certificate2(
                pfxPath,
                CaPassword,
                X509KeyStorageFlags.Exportable);
            var expiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var now = DateTimeOffset.UtcNow;

            if (expiresAt <= now)
            {
                return CreateCertificateDiagnostic(
                    project,
                    host,
                    certificatePath,
                    "Blocked",
                    "Expired",
                    $"Certificate for {host} has expired.",
                    $"The certificate expired at {expiresAt.LocalDateTime:g}.",
                    "Run Repair Local SSL to renew this project certificate.",
                    expiresAt);
            }

            if (!CertificateMatchesHost(certificate, host))
            {
                return CreateCertificateDiagnostic(
                    project,
                    host,
                    certificatePath,
                    "Blocked",
                    "Invalid",
                    $"Certificate for {host} does not match the project host.",
                    "The certificate subject does not match the generated local domain.",
                    "Run Repair Local SSL to recreate this certificate with the current project domain.",
                    expiresAt);
            }

            if (caLastGeneratedAt is not null && File.GetLastWriteTimeUtc(pfxPath) < caLastGeneratedAt.Value.UtcDateTime)
            {
                return CreateCertificateDiagnostic(
                    project,
                    host,
                    certificatePath,
                    "Blocked",
                    "StaleAuthority",
                    $"Certificate for {host} was generated before the current Locora CA.",
                    "The CA was regenerated after this project certificate, so browsers may not trust the project certificate chain.",
                    "Run Repair Local SSL to reissue this project certificate from the current CA.",
                    expiresAt);
            }

            if (expiresAt <= now.Add(CertificateRenewalWindow))
            {
                return CreateCertificateDiagnostic(
                    project,
                    host,
                    certificatePath,
                    "Attention",
                    "ExpiringSoon",
                    $"Certificate for {host} expires soon.",
                    $"The certificate expires at {expiresAt.LocalDateTime:g}.",
                    "Run Repair Local SSL to renew this project certificate before it expires.",
                    expiresAt);
            }

            return CreateCertificateDiagnostic(
                project,
                host,
                certificatePath,
                "Ready",
                "Ready",
                $"Certificate for {host} is valid.",
                $"The certificate expires at {expiresAt.LocalDateTime:g}.",
                "No action needed.",
                expiresAt);
        }
        catch (Exception exception)
        {
            return CreateCertificateDiagnostic(
                project,
                host,
                certificatePath,
                "Blocked",
                "Invalid",
                $"Certificate material for {host} could not be read.",
                exception.Message,
                "Run Repair Local SSL to recreate this project certificate material.",
                null);
        }
    }

    private static LocalSslCertificateDiagnosticSnapshot CreateCertificateDiagnostic(
        DiscoveredProject project,
        string host,
        string certificatePath,
        string state,
        string reason,
        string summary,
        string details,
        string suggestedAction,
        DateTimeOffset? expiresAt)
    {
        return new LocalSslCertificateDiagnosticSnapshot(
            $"ssl_{project.Slug}_{reason.ToLowerInvariant()}",
            project.Name,
            host,
            state,
            reason,
            summary,
            details,
            suggestedAction,
            certificatePath,
            expiresAt);
    }

    private static bool IsRepairable(LocalSslCertificateDiagnosticSnapshot diagnostic)
    {
        return !diagnostic.State.Equals("Ready", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CertificateMatchesHost(X509Certificate2 certificate, string host)
    {
        var dnsName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        return string.Equals(dnsName, host, StringComparison.OrdinalIgnoreCase) ||
            certificate.Subject.Contains($"CN={host}", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureProjectCertificate(DiscoveredProject project, X509Certificate2 certificateAuthority)
    {
        var host = new Uri(project.Url).Host;
        var materialRoot = Path.Combine(GetCertificatesRoot(), project.Slug);
        var certificatePath = Path.Combine(materialRoot, $"{host}.crt.pem");
        var keyPath = Path.Combine(materialRoot, $"{host}.key.pem");
        var pfxPath = Path.Combine(materialRoot, $"{host}.pfx");

        Directory.CreateDirectory(materialRoot);

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={host}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        var enhancedKeyUsage = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1")
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsage, critical: true));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName(host);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddYears(3);
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var issuedCertificate = request.Create(certificateAuthority, notBefore, notAfter, serialNumber);
        using var certificateWithPrivateKey = issuedCertificate.CopyWithPrivateKey(key);
        var exportableCertificate = new X509Certificate2(
            certificateWithPrivateKey.Export(X509ContentType.Pfx, CaPassword),
            CaPassword,
            X509KeyStorageFlags.Exportable);

        File.WriteAllBytes(pfxPath, exportableCertificate.Export(X509ContentType.Pfx, CaPassword));
        File.WriteAllText(certificatePath, exportableCertificate.ExportCertificatePem() + Environment.NewLine + certificateAuthority.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
    }

    private X509Certificate2 EnsureCertificateAuthority()
    {
        if (TryLoadUsableCertificateAuthority(out var certificateAuthority, out _))
        {
            return certificateAuthority;
        }

        return CreateCertificateAuthority();
    }

    private X509Certificate2 CreateCertificateAuthority()
    {
        var caRoot = GetCaRoot();
        var pfxPath = GetCaPfxPath();
        Directory.CreateDirectory(caRoot);

        using var key = RSA.Create(4096);
        var request = new CertificateRequest(
            $"CN={AuthorityName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddYears(10);
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var exportableCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Pfx, CaPassword),
            CaPassword,
            X509KeyStorageFlags.Exportable);

        File.WriteAllBytes(pfxPath, exportableCertificate.Export(X509ContentType.Pfx, CaPassword));
        File.WriteAllText(GetCaCertificatePath(), exportableCertificate.ExportCertificatePem());
        File.WriteAllText(GetCaKeyPath(), key.ExportPkcs8PrivateKeyPem());

        return exportableCertificate;
    }

    private bool IsCertificateAuthorityUsable()
    {
        if (!TryLoadUsableCertificateAuthority(out var certificateAuthority, out _))
        {
            return false;
        }

        certificateAuthority.Dispose();
        return true;
    }

    private bool TryLoadUsableCertificateAuthority(out X509Certificate2 certificateAuthority, out string failureReason)
    {
        certificateAuthority = null!;

        if (!File.Exists(GetCaPfxPath()) || !File.Exists(GetCaCertificatePath()) || !File.Exists(GetCaKeyPath()))
        {
            failureReason = "Local SSL CA files are missing.";
            return false;
        }

        try
        {
            certificateAuthority = LoadCertificateAuthority();
            var expiresAt = new DateTimeOffset(certificateAuthority.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            if (expiresAt <= DateTimeOffset.UtcNow.Add(CertificateRenewalWindow))
            {
                failureReason = $"Local SSL CA expires at {expiresAt.LocalDateTime:g}.";
                certificateAuthority.Dispose();
                certificateAuthority = null!;
                return false;
            }

            failureReason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            certificateAuthority.Dispose();
            certificateAuthority = null!;
            return false;
        }
    }

    private X509Certificate2 LoadCertificateAuthority()
    {
        return new X509Certificate2(
            GetCaPfxPath(),
            CaPassword,
            X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 LoadCertificateAuthorityPublicCertificate()
    {
        return new X509Certificate2(GetCaCertificatePath());
    }

    private bool IsCurrentUserTrusted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return FindTrustedCertificates(store).Any();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to inspect CurrentUser root store for local SSL trust.");
            return false;
        }
    }

    private IEnumerable<X509Certificate2> FindTrustedCertificates(X509Store store)
    {
        var thumbprint = GetCertificateAuthorityThumbprint();

        return store.Certificates
            .OfType<X509Certificate2>()
            .Where(certificate => !string.IsNullOrWhiteSpace(thumbprint)
                ? string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)
                : string.Equals(certificate.Subject, $"CN={AuthorityName}", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string? GetCertificateAuthorityThumbprint()
    {
        try
        {
            if (File.Exists(GetCaCertificatePath()))
            {
                using var caCertificate = LoadCertificateAuthorityPublicCertificate();
                return caCertificate.Thumbprint;
            }

            if (File.Exists(GetCaPfxPath()))
            {
                using var caCertificate = LoadCertificateAuthority();
                return caCertificate.Thumbprint;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to read local SSL CA thumbprint.");
        }

        return null;
    }

    private ProjectCertificateMaterial? GetProjectCertificateMaterial(string slug, string host)
    {
        var materialRoot = Path.Combine(GetCertificatesRoot(), slug);
        var certificatePath = Path.Combine(materialRoot, $"{host}.crt.pem");
        var keyPath = Path.Combine(materialRoot, $"{host}.key.pem");
        var pfxPath = Path.Combine(materialRoot, $"{host}.pfx");

        if (!File.Exists(certificatePath) || !File.Exists(keyPath) || !File.Exists(pfxPath))
        {
            return null;
        }

        var lastGeneratedAt = new[]
        {
            File.GetLastWriteTimeUtc(certificatePath),
            File.GetLastWriteTimeUtc(keyPath),
            File.GetLastWriteTimeUtc(pfxPath)
        }.Max();

        return new ProjectCertificateMaterial(
            host,
            certificatePath,
            keyPath,
            pfxPath,
            new DateTimeOffset(lastGeneratedAt, TimeSpan.Zero));
    }

    private static DateTimeOffset? GetLastGeneratedAt(string? caCertificatePath, IReadOnlyList<ProjectCertificateMaterial> materials)
    {
        var timestamps = new List<DateTimeOffset>();

        if (!string.IsNullOrWhiteSpace(caCertificatePath) && File.Exists(caCertificatePath))
        {
            timestamps.Add(new DateTimeOffset(File.GetLastWriteTimeUtc(caCertificatePath), TimeSpan.Zero));
        }

        timestamps.AddRange(materials.Select(material => material.LastGeneratedAt));

        return timestamps.Count > 0 ? timestamps.Max() : null;
    }

    private DateTimeOffset? GetCaLastGeneratedAt()
    {
        var caCertificatePath = GetCaCertificatePath();
        return File.Exists(caCertificatePath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(caCertificatePath), TimeSpan.Zero)
            : null;
    }

    private DateTimeOffset? GetCaExpiresAt()
    {
        try
        {
            if (!File.Exists(GetCaCertificatePath()))
            {
                return null;
            }

            using var certificate = LoadCertificateAuthorityPublicCertificate();
            return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateSummary(
        bool caExists,
        bool caUsable,
        bool trusted,
        bool trustSupported,
        int projectCertificateCount,
        int missingProjectCertificateCount,
        int expiredProjectCertificateCount,
        int expiringProjectCertificateCount)
    {
        if (!caExists)
        {
            return "Local SSL has not been generated yet";
        }

        if (!caUsable)
        {
            return "Local SSL CA needs repair";
        }

        if (expiredProjectCertificateCount > 0 || missingProjectCertificateCount > 0)
        {
            return $"Local SSL needs repair: {missingProjectCertificateCount} missing and {expiredProjectCertificateCount} expired or invalid project certificates";
        }

        if (expiringProjectCertificateCount > 0)
        {
            return $"Local SSL is working, but {expiringProjectCertificateCount} project certificates expire soon";
        }

        if (!trustSupported)
        {
            return "Local SSL generated; trust state unavailable on this OS";
        }

        if (!trusted)
        {
            return "Local SSL certificates are ready, but the CA is not trusted";
        }

        return projectCertificateCount == 0
            ? "Local SSL CA is ready"
            : $"Local SSL is ready for {projectCertificateCount} projects";
    }

    private static string CreateDetails(
        bool caExists,
        bool caUsable,
        bool trusted,
        bool trustSupported,
        int projectCertificateCount,
        int httpsProjectCount,
        int missingProjectCertificateCount,
        int expiredProjectCertificateCount,
        int expiringProjectCertificateCount)
    {
        if (!caExists)
        {
            return "Generate a Locora certificate authority and per-project certificates to enable HTTPS vhosts.";
        }

        if (!caUsable)
        {
            return "Run Repair Local SSL to recreate the Locora CA and regenerate project certificates.";
        }

        if (expiredProjectCertificateCount > 0 || missingProjectCertificateCount > 0)
        {
            return "Run Repair Local SSL to recreate missing, expired, invalid, or stale project certificate material and regenerate HTTPS vhosts.";
        }

        if (expiringProjectCertificateCount > 0)
        {
            return "Run Repair Local SSL to renew certificates that are inside the 30-day renewal window.";
        }

        if (!trustSupported)
        {
            return "Certificate files were generated, but trust-store checks are only available on Windows.";
        }

        if (!trusted)
        {
            return "Trust the Locora CA in the CurrentUser root store to remove browser warnings for generated HTTPS domains.";
        }

        if (projectCertificateCount == 0)
        {
            return "The CA exists and is trusted, but no project certificates have been generated yet.";
        }

        return $"HTTPS certificate material is available for {projectCertificateCount} projects, and {httpsProjectCount} discovered projects now resolve to https:// URLs.";
    }

    private string GetCaRoot() => Path.Combine(_paths.ConfigRoot, "ssl", "ca");

    private string GetCertificatesRoot() => Path.Combine(_paths.ConfigRoot, "ssl", "certs");

    private string GetCaCertificatePath() => Path.Combine(GetCaRoot(), "locora-root-ca.cer");

    private string GetCaKeyPath() => Path.Combine(GetCaRoot(), "locora-root-ca.key.pem");

    private string GetCaPfxPath() => Path.Combine(GetCaRoot(), "locora-root-ca.pfx");
}
