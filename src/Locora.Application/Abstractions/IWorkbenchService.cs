using Locora.Domain.Entities;

namespace Locora.Application.Abstractions;

public interface IWorkbenchService
{
    Task<EnvironmentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StartAllAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StopAllAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ApplyHostsPreviewAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RollbackHostsAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> ValidateNginxConfigAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairRuntimeAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RepairLocalSslAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> GenerateLocalSslAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> TrustLocalSslCaAsync(CancellationToken cancellationToken = default);

    Task<EnvironmentSnapshot> RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default);
}
