using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class SupervisorRuntimeBootstrapper : IHostedService
{
    private readonly ManagedServiceRegistry _registry;
    private readonly ILogger<SupervisorRuntimeBootstrapper> _logger;

    public SupervisorRuntimeBootstrapper(
        ManagedServiceRegistry registry,
        ILogger<SupervisorRuntimeBootstrapper> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting auto-start services.");
        await _registry.StartAutoStartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping managed services.");
        await _registry.StopAllAsync(cancellationToken);
    }
}
