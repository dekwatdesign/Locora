using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class RuntimeRepairService
{
    private readonly ManagedServiceRegistry _registry;
    private readonly ServiceConfigurationWriter _serviceConfigurationWriter;
    private readonly ProjectDiscoveryService _projectDiscoveryService;
    private readonly ProjectConfigurationWriter _projectConfigurationWriter;
    private readonly LocalSslService _localSslService;
    private readonly NginxConfigValidator _nginxConfigValidator;
    private readonly ILogger<RuntimeRepairService> _logger;

    public RuntimeRepairService(
        ManagedServiceRegistry registry,
        ServiceConfigurationWriter serviceConfigurationWriter,
        ProjectDiscoveryService projectDiscoveryService,
        ProjectConfigurationWriter projectConfigurationWriter,
        LocalSslService localSslService,
        NginxConfigValidator nginxConfigValidator,
        ILogger<RuntimeRepairService> logger)
    {
        _registry = registry;
        _serviceConfigurationWriter = serviceConfigurationWriter;
        _projectDiscoveryService = projectDiscoveryService;
        _projectConfigurationWriter = projectConfigurationWriter;
        _localSslService = localSslService;
        _nginxConfigValidator = nginxConfigValidator;
        _logger = logger;
    }

    public async Task RepairCommonIssuesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Repairing common runtime configuration issues.");
        await _serviceConfigurationWriter.GenerateAllAsync(cancellationToken);
        await RepairDomainsAsync(cancellationToken);
    }

    public async Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        var service = _registry.Services.FirstOrDefault(candidate =>
            candidate.Definition.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            throw new KeyNotFoundException($"Managed service '{serviceKey}' is not configured.");
        }

        if (!await _serviceConfigurationWriter.GenerateForServiceAsync(serviceKey, cancellationToken))
        {
            throw new InvalidOperationException($"{service.Definition.DisplayName} does not support automatic repair yet.");
        }

        if (IsNginxService(service))
        {
            await RegenerateProjectArtifactsAsync(cancellationToken);
            await _nginxConfigValidator.ValidateAsync(cancellationToken);
        }

        _logger.LogInformation("Repaired generated runtime artifacts for {Service}", service.Definition.DisplayName);
    }

    public async Task RepairDomainsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Repairing generated hosts and vhost artifacts.");
        await RegenerateProjectArtifactsAsync(cancellationToken);
        await _nginxConfigValidator.ValidateAsync(cancellationToken);
    }

    private async Task RegenerateProjectArtifactsAsync(CancellationToken cancellationToken)
    {
        var projects = await _projectDiscoveryService.DiscoverAsync(cancellationToken);
        var sslAwareProjects = _localSslService.ApplySsl(projects);
        await _projectConfigurationWriter.GenerateAsync(sslAwareProjects, cancellationToken);
    }

    private static bool IsNginxService(IManagedService service)
    {
        return service.Definition.Kind.Equals("nginx", StringComparison.OrdinalIgnoreCase) ||
            service.Definition.Key.Equals("nginx", StringComparison.OrdinalIgnoreCase);
    }
}
