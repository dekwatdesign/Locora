using System.Text.Json;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class StackProfileRegistry
{
    private static readonly JsonSerializerOptions ReadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IEnvironmentPaths _paths;
    private readonly ILogger<StackProfileRegistry> _logger;

    public StackProfileRegistry(IEnvironmentPaths paths, ILogger<StackProfileRegistry> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<StackProfileRegistrySnapshot> GetSnapshotAsync(
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        return BuildSnapshot(document.LocoraProfiles, configuredServices);
    }

    public async Task<StackProfileDefinition> GetActiveProfileAsync(
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(configuredServices, cancellationToken);
        var profile = snapshot.Profiles.FirstOrDefault(profile => profile.IsActive) ??
            snapshot.Profiles.FirstOrDefault();
        if (profile is null)
        {
            return new StackProfileDefinition
            {
                Key = "default",
                DisplayName = "Default",
                Description = "Fallback profile generated because no profiles are configured.",
                ServiceKeys = configuredServices.Select(service => service.Definition.Key).ToList()
            };
        }

        return profile.Definition;
    }

    public async Task<SelectStackProfileResult> SelectProfileAsync(
        string profileKey,
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            throw new ArgumentException("Profile key is required.", nameof(profileKey));
        }

        var document = await ReadDocumentAsync(cancellationToken);
        var profile = document.LocoraProfiles.Profiles.FirstOrDefault(profile =>
            profile.Key.Equals(profileKey, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new KeyNotFoundException($"Stack profile '{profileKey}' is not configured.");
        }

        await WriteDocumentAsync(
            new StackProfilesDocument
            {
                LocoraProfiles = new StackProfilesOptions
                {
                    SchemaVersion = document.LocoraProfiles.SchemaVersion,
                    ActiveProfileKey = profile.Key,
                    Profiles = document.LocoraProfiles.Profiles
                }
            },
            cancellationToken);

        _logger.LogInformation("Selected stack profile {ProfileKey}.", profile.Key);
        var nextOptions = new StackProfilesOptions
        {
            SchemaVersion = document.LocoraProfiles.SchemaVersion,
            ActiveProfileKey = profile.Key,
            Profiles = document.LocoraProfiles.Profiles
        };
        return new SelectStackProfileResult(profile, BuildSnapshot(nextOptions, configuredServices));
    }

    private StackProfileRegistrySnapshot BuildSnapshot(
        StackProfilesOptions options,
        IReadOnlyCollection<IManagedService> configuredServices)
    {
        var serviceKeys = configuredServices
            .Select(service => service.Definition.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profiles = options.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Key))
            .Select(profile => BuildProfileSnapshot(profile, options.ActiveProfileKey, serviceKeys))
            .OrderByDescending(profile => profile.IsActive)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activeProfile = profiles.FirstOrDefault(profile => profile.IsActive) ?? profiles.FirstOrDefault();
        var activeProfileKey = activeProfile?.Key ?? options.ActiveProfileKey;
        var activeProfileName = activeProfile?.DisplayName ?? "No active profile";
        var validCount = profiles.Count(profile => profile.IsValid);
        var invalidCount = profiles.Length - validCount;

        return new StackProfileRegistrySnapshot(
            profiles,
            profiles.Length,
            validCount,
            invalidCount,
            activeProfileKey,
            activeProfileName,
            profiles.Length == 0
                ? "No stack profiles configured"
                : $"{profiles.Length} stack profile{(profiles.Length == 1 ? string.Empty : "s")} configured / active {activeProfileName}",
            invalidCount == 0
                ? "All stack profile service references match configured services."
                : $"{invalidCount} profile{(invalidCount == 1 ? string.Empty : "s")} reference missing services.");
    }

    private static StackProfileStatusSnapshot BuildProfileSnapshot(
        StackProfileDefinition profile,
        string activeProfileKey,
        HashSet<string> configuredServiceKeys)
    {
        var missingServices = profile.ServiceKeys
            .Where(serviceKey => !configuredServiceKeys.Contains(serviceKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var isActive = profile.Key.Equals(activeProfileKey, StringComparison.OrdinalIgnoreCase);
        var isValid = missingServices.Length == 0;
        var serviceLabel = profile.ServiceKeys.Count == 0
            ? "all configured services"
            : string.Join(", ", profile.ServiceKeys);
        var packageLabel = profile.PackageSelections.Count == 0
            ? "no package selections"
            : string.Join(", ", profile.PackageSelections.Select(selection => $"{selection.Key} {selection.Value}"));

        return new StackProfileStatusSnapshot(
            profile,
            profile.Key,
            string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Key : profile.DisplayName,
            profile.Description,
            isActive ? "Active" : isValid ? "Ready" : "Invalid",
            profile.ServiceKeys,
            profile.PackageSelections,
            profile.Tags,
            isActive,
            isValid,
            isValid
                ? $"{serviceLabel}; {packageLabel}"
                : $"Missing services: {string.Join(", ", missingServices)}",
            isValid
                ? "Selecting this profile updates the active profile and merges its package selections into packages.lock.json."
                : "Edit profiles.json or services.json so every service key exists before selecting this profile.");
    }

    private async Task<StackProfilesDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_paths.ProfilesSettingsFile))
            {
                return new StackProfilesDocument();
            }

            await using var stream = File.OpenRead(_paths.ProfilesSettingsFile);
            return await JsonSerializer.DeserializeAsync<StackProfilesDocument>(stream, ReadSerializerOptions, cancellationToken) ??
                new StackProfilesDocument();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read stack profiles from {Path}.", _paths.ProfilesSettingsFile);
            return new StackProfilesDocument();
        }
    }

    private async Task WriteDocumentAsync(StackProfilesDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.ProfilesSettingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_paths.ProfilesSettingsFile);
        await JsonSerializer.SerializeAsync(stream, document, WriteSerializerOptions, cancellationToken);
    }
}

public sealed record StackProfileRegistrySnapshot(
    IReadOnlyList<StackProfileStatusSnapshot> Profiles,
    int ProfileCount,
    int ValidProfileCount,
    int InvalidProfileCount,
    string ActiveProfileKey,
    string ActiveProfileName,
    string Summary,
    string Details);

public sealed record StackProfileStatusSnapshot(
    StackProfileDefinition Definition,
    string Key,
    string DisplayName,
    string Description,
    string State,
    IReadOnlyList<string> ServiceKeys,
    IReadOnlyDictionary<string, string> PackageSelections,
    IReadOnlyList<string> Tags,
    bool IsActive,
    bool IsValid,
    string Summary,
    string Details);

public sealed record SelectStackProfileResult(
    StackProfileDefinition Profile,
    StackProfileRegistrySnapshot Snapshot);
