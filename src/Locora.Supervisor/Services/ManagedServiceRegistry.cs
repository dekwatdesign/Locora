using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ManagedServiceRegistry
{
    private readonly IReadOnlyDictionary<string, IManagedService> _services;
    private readonly IManagedService[] _serviceList;
    private readonly IReadOnlyList<ServicePresetDefinition> _presets;
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
        _presets = NormalizePresets(options.Value.Presets);

        _nginxConfigValidator = nginxConfigValidator;
        _logger = logger;
    }

    public IReadOnlyCollection<IManagedService> Services => _serviceList;

    public IReadOnlyList<ServicePresetSnapshot> Presets => _presets
        .Select(BuildPresetSnapshot)
        .OrderBy(preset => preset.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

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
                _logger.LogWarning("Service group requested service {ServiceKey}, but it is not configured.", serviceKey);
                continue;
            }

            await StartWithPreflightAsync(service, cancellationToken);
        }
    }

    public async Task StartPresetAsync(string presetKey, CancellationToken cancellationToken = default)
    {
        var preset = GetRequiredPreset(presetKey);
        await StartServicesAsync(preset.ServiceKeys, cancellationToken);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        for (var index = _serviceList.Length - 1; index >= 0; index--)
        {
            await _serviceList[index].StopAsync(cancellationToken);
        }
    }

    public async Task StopServicesAsync(IEnumerable<string> serviceKeys, CancellationToken cancellationToken = default)
    {
        var requestedKeys = serviceKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .ToArray();
        if (requestedKeys.Length == 0)
        {
            await StopAllAsync(cancellationToken);
            return;
        }

        foreach (var serviceKey in requestedKeys)
        {
            if (!_services.TryGetValue(serviceKey, out var service))
            {
                _logger.LogWarning("Preset requested service {ServiceKey}, but it is not configured.", serviceKey);
                continue;
            }

            await service.StopAsync(cancellationToken);
        }
    }

    public async Task StopPresetAsync(string presetKey, CancellationToken cancellationToken = default)
    {
        var preset = GetRequiredPreset(presetKey);
        await StopServicesAsync(preset.ServiceKeys, cancellationToken);
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

    private ServicePresetDefinition GetRequiredPreset(string presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            throw new ArgumentException("Service preset key is required.", nameof(presetKey));
        }

        var preset = _presets.FirstOrDefault(preset =>
            preset.Key.Equals(presetKey, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            throw new KeyNotFoundException($"Service preset '{presetKey}' is not configured.");
        }

        var snapshot = BuildPresetSnapshot(preset);
        if (!snapshot.IsValid)
        {
            throw new InvalidOperationException($"Service preset '{presetKey}' is invalid: {snapshot.Details}");
        }

        return preset;
    }

    private ServicePresetSnapshot BuildPresetSnapshot(ServicePresetDefinition preset)
    {
        var missingServices = preset.ServiceKeys
            .Where(serviceKey => !_services.ContainsKey(serviceKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var serviceNames = preset.ServiceKeys
            .Select(serviceKey => _services.TryGetValue(serviceKey, out var service)
                ? service.Definition.DisplayName
                : serviceKey)
            .ToArray();
        var isValid = preset.ServiceKeys.Count > 0 && missingServices.Length == 0;
        var servicesLabel = serviceNames.Length == 0
            ? "No services selected"
            : string.Join(", ", serviceNames);

        return new ServicePresetSnapshot(
            preset,
            preset.Key,
            string.IsNullOrWhiteSpace(preset.DisplayName) ? preset.Key : preset.DisplayName,
            preset.Description,
            preset.ServiceKeys,
            preset.Tags,
            isValid,
            isValid ? "Ready" : "Invalid",
            servicesLabel,
            isValid
                ? $"{serviceNames.Length} service{(serviceNames.Length == 1 ? string.Empty : "s")}: {servicesLabel}"
                : missingServices.Length == 0
                    ? "No services are assigned to this preset."
                    : $"Missing services: {string.Join(", ", missingServices)}",
            isValid
                ? "Starting this preset starts only these services; stopping it stops the same services in reverse order."
                : "Edit services.json so the preset has at least one valid service key.");
    }

    private static IReadOnlyList<ServicePresetDefinition> NormalizePresets(IEnumerable<ServicePresetDefinition>? presets)
    {
        if (presets is null)
        {
            return [];
        }

        return presets
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Key))
            .Select(preset => new ServicePresetDefinition
            {
                Key = preset.Key.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(preset.DisplayName) ? preset.Key.Trim() : preset.DisplayName.Trim(),
                Description = preset.Description?.Trim() ?? string.Empty,
                ServiceKeys = NormalizeStringList(preset.ServiceKeys),
                Tags = NormalizeStringList(preset.Tags)
            })
            .GroupBy(preset => preset.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed record ServicePresetSnapshot(
    ServicePresetDefinition Definition,
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> ServiceKeys,
    IReadOnlyList<string> Tags,
    bool IsValid,
    string State,
    string ServicesLabel,
    string Summary,
    string Details);
