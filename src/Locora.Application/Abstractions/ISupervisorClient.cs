using Locora.App.Contracts;

namespace Locora.Application.Abstractions;

public interface ISupervisorClient
{
    Task<EnvironmentSnapshotDto> GetEnvironmentSnapshotAsync(CancellationToken cancellationToken = default);

    Task StartAllAsync(CancellationToken cancellationToken = default);

    Task StopAllAsync(CancellationToken cancellationToken = default);

    Task StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task ApplyHostsPreviewAsync(CancellationToken cancellationToken = default);

    Task RollbackHostsAsync(CancellationToken cancellationToken = default);

    Task RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default);

    Task ValidateNginxConfigAsync(CancellationToken cancellationToken = default);

    Task RepairRuntimeAsync(CancellationToken cancellationToken = default);

    Task RepairDomainsAsync(CancellationToken cancellationToken = default);

    Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task SyncPackageDownloadsAsync(CancellationToken cancellationToken = default);

    Task ExtractPackageArchivesAsync(CancellationToken cancellationToken = default);

    Task InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default);

    Task RemovePackageInstallAsync(string packageId, CancellationToken cancellationToken = default);

    Task SelectRuntimeVersionAsync(string selectionKey, CancellationToken cancellationToken = default);

    Task SelectStackProfileAsync(string profileKey, CancellationToken cancellationToken = default);

    Task RepairLocalSslAsync(CancellationToken cancellationToken = default);

    Task GenerateLocalSslAsync(CancellationToken cancellationToken = default);

    Task TrustLocalSslCaAsync(CancellationToken cancellationToken = default);

    Task RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default);
}
