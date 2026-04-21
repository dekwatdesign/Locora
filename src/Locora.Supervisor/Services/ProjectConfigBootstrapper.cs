using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class ProjectConfigBootstrapper : IHostedService
{
    private readonly ProjectDiscoveryService _projectDiscovery;
    private readonly ProjectConfigurationWriter _configurationWriter;
    private readonly ILogger<ProjectConfigBootstrapper> _logger;

    public ProjectConfigBootstrapper(
        ProjectDiscoveryService projectDiscovery,
        ProjectConfigurationWriter configurationWriter,
        ILogger<ProjectConfigBootstrapper> logger)
    {
        _projectDiscovery = projectDiscovery;
        _configurationWriter = configurationWriter;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var projects = await _projectDiscovery.DiscoverAsync(cancellationToken);
        await _configurationWriter.GenerateAsync(projects, cancellationToken);
        _logger.LogInformation("Generated project config for {Count} discovered projects.", projects.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
