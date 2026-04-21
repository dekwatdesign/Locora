using Locora.Supervisor.Configuration;

namespace Locora.Supervisor.Services;

public interface IManagedService
{
    ManagedServiceDefinition Definition { get; }

    Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
