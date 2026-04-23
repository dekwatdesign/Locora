using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ManagedServiceRegistry
{
    private readonly IReadOnlyDictionary<string, IManagedService> _services;
    private readonly IManagedService[] _serviceList;
    private readonly NginxConfigValidator _nginxConfigValidator;
    private readonly ILogger<ManagedServiceRegistry> _logger;

    public ManagedServiceRegistry(
        IOptions<ManagedServicesOptions> options,
        ManagedServiceFactory factory,
        NginxConfigValidator nginxConfigValidator,
        ILogger<ManagedServiceRegistry> logger)
    {
        _services = options.Value.Services
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Key))
            .ToDictionary(definition => definition.Key, factory.Create, StringComparer.OrdinalIgnoreCase);
        _serviceList = _services.Values.ToArray();

        _nginxConfigValidator = nginxConfigValidator;
        _logger = logger;
    }

    public IReadOnlyCollection<IManagedService> Services => _serviceList;

    public async Task<IReadOnlyList<ManagedServiceStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _serviceList.Select(service => service.GetStatusAsync(cancellationToken));
        return await Task.WhenAll(tasks);
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var service in _serviceList)
        {
            await StartWithPreflightAsync(service, cancellationToken);
        }
    }

    public async Task StartServicesAsync(IEnumerable<string> serviceKeys, CancellationToken cancellationToken = default)
    {
        var requestedKeys = serviceKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedKeys.Length == 0)
        {
            await StartAllAsync(cancellationToken);
            return;
        }

        foreach (var serviceKey in requestedKeys)
        {
            if (!_services.TryGetValue(serviceKey, out var service))
            {
                _logger.LogWarning("Profile requested service {ServiceKey}, but it is not configured.", serviceKey);
                continue;
            }

            await StartWithPreflightAsync(service, cancellationToken);
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        for (var index = _serviceList.Length - 1; index >= 0; index--)
        {
            await _serviceList[index].StopAsync(cancellationToken);
        }
    }

    public async Task StartAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        var service = GetRequiredService(serviceKey);
        _logger.LogInformation("Starting {Service}", service.Definition.DisplayName);
        await StartWithPreflightAsync(service, cancellationToken);
    }

    public async Task StopAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        var service = GetRequiredService(serviceKey);
        _logger.LogInformation("Stopping {Service}", service.Definition.DisplayName);
        await service.StopAsync(cancellationToken);
    }

    public async Task StartAutoStartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var service in _serviceList.Where(service => service.Definition.AutoStart))
        {
            _logger.LogInformation("Auto-starting {Service}", service.Definition.DisplayName);
            await StartWithPreflightAsync(service, cancellationToken);
        }
    }

    private async Task StartWithPreflightAsync(IManagedService service, CancellationToken cancellationToken)
    {
        if (await ShouldSkipStartAsync(service, cancellationToken))
        {
            return;
        }

        await service.StartAsync(cancellationToken);
    }

    private async Task<bool> ShouldSkipStartAsync(IManagedService service, CancellationToken cancellationToken)
    {
        if (!IsNginxService(service))
        {
            return false;
        }

        var result = await _nginxConfigValidator.ValidateAsync(cancellationToken);
        if (result.IsValid)
        {
            return false;
        }

        _logger.LogWarning(
            "Skipped starting {Service} because config validation failed: {Summary}",
            service.Definition.DisplayName,
            result.Summary);
        return true;
    }

    private static bool IsNginxService(IManagedService service)
    {
        return service.Definition.Kind.Equals("nginx", StringComparison.OrdinalIgnoreCase) ||
            service.Definition.Key.Equals("nginx", StringComparison.OrdinalIgnoreCase);
    }

    private IManagedService GetRequiredService(string serviceKey)
    {
        if (_services.TryGetValue(serviceKey, out var service))
        {
            return service;
        }

        throw new KeyNotFoundException($"Managed service '{serviceKey}' is not configured.");
    }
}
