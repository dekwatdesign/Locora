using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class SupervisorConfigBootstrapper : IHostedService
{
    private readonly ServiceConfigurationWriter _configurationWriter;
    private readonly ILogger<SupervisorConfigBootstrapper> _logger;

    public SupervisorConfigBootstrapper(
        ServiceConfigurationWriter configurationWriter,
        ILogger<SupervisorConfigBootstrapper> logger)
    {
        _configurationWriter = configurationWriter;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating service configs for managed runtimes.");
        await _configurationWriter.GenerateAllAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
