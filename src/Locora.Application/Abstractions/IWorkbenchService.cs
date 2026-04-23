using Locora.Domain.Entities;

namespace Locora.Application.Abstractions;

public interface IWorkbenchService
{
    Task<EnvironmentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StartAllAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StopAllAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StartServicePresetAsync(string presetKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StopServicePresetAsync(string presetKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ApplyHostsPreviewAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RollbackHostsAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ValidateNginxConfigAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairRuntimeAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairDomainsAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> SyncPackageDownloadsAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ExtractPackageArchivesAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RemovePackageInstallAsync(string packageId, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> SelectRuntimeVersionAsync(string packageId, string version, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> SelectStackProfileAsync(string profileKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> SaveActiveEnvironmentAsync(string profileName, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ExportStackProfilesAsync(string targetPath, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ImportStackProfilesAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairLocalSslAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> GenerateLocalSslAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> TrustLocalSslCaAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default);
}
