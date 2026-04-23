using System.Globalization;
using System.Text;
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

    public async Task<SaveActiveEnvironmentProfileResult> SaveActiveEnvironmentAsync(
        string displayName,
        IReadOnlyCollection<string> serviceKeys,
        IReadOnlyDictionary<string, string> packageSelections,
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Profile name is required.", nameof(displayName));
        }

        var document = await ReadDocumentAsync(cancellationToken);
        var existingKeys = document.LocoraProfiles.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Key))
            .Select(profile => profile.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profileKey = CreateUniqueProfileKey(displayName, existingKeys);
        var configuredServiceKeys = configuredServices
            .Select(service => service.Definition.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedServiceKeys = serviceKeys
            .Where(serviceKey => !string.IsNullOrWhiteSpace(serviceKey) && configuredServiceKeys.Contains(serviceKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(serviceKey => serviceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedServiceKeys.Count == 0)
        {
            normalizedServiceKeys = configuredServices
                .Where(service => service.Definition.AutoStart)
                .Select(service => service.Definition.Key)
                .Where(serviceKey => !string.IsNullOrWhiteSpace(serviceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(serviceKey => serviceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (normalizedServiceKeys.Count == 0)
        {
            normalizedServiceKeys = configuredServices
                .Select(service => service.Definition.Key)
                .Where(serviceKey => !string.IsNullOrWhiteSpace(serviceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(serviceKey => serviceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var normalizedPackageSelections = packageSelections
            .Where(selection => !string.IsNullOrWhiteSpace(selection.Key))
            .GroupBy(selection => selection.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(selection => selection.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                selection => selection.Key,
                selection => selection.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var savedAt = DateTimeOffset.Now.ToString("g", CultureInfo.CurrentCulture);
        var profile = new StackProfileDefinition
        {
            Key = profileKey,
            DisplayName = displayName.Trim(),
            Description = $"Saved from the active Locora environment on {savedAt}.",
            ServiceKeys = normalizedServiceKeys,
            PackageSelections = normalizedPackageSelections,
            Tags = ["saved", "active-environment"]
        };
        var nextProfiles = document.LocoraProfiles.Profiles
            .Concat([profile])
            .ToList();
        var nextOptions = new StackProfilesOptions
        {
            SchemaVersion = document.LocoraProfiles.SchemaVersion,
            ActiveProfileKey = profile.Key,
            Profiles = nextProfiles
        };

        await WriteDocumentAsync(
            new StackProfilesDocument
            {
                LocoraProfiles = nextOptions
            },
            cancellationToken);

        _logger.LogInformation("Saved active environment as stack profile {ProfileKey}.", profile.Key);
        return new SaveActiveEnvironmentProfileResult(profile, BuildSnapshot(nextOptions, configuredServices));
    }

    public async Task<ExportStackProfilesResult> ExportProfilesAsync(
        string targetPath,
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveTransferPath(targetPath);
        var document = await ReadDocumentAsync(cancellationToken);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(resolvedPath);
        await JsonSerializer.SerializeAsync(stream, document, WriteSerializerOptions, cancellationToken);

        _logger.LogInformation("Exported {ProfileCount} stack profiles to {Path}.", document.LocoraProfiles.Profiles.Count, resolvedPath);
        return new ExportStackProfilesResult(
            resolvedPath,
            document.LocoraProfiles.Profiles.Count,
            BuildSnapshot(document.LocoraProfiles, configuredServices));
    }

    public async Task<ImportStackProfilesResult> ImportProfilesAsync(
        string sourcePath,
        IReadOnlyCollection<IManagedService> configuredServices,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveTransferPath(sourcePath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Stack profile import file was not found: {resolvedPath}", resolvedPath);
        }

        var importedDocument = await ReadDocumentFileAsync(resolvedPath, cancellationToken);
        var currentDocument = await ReadDocumentAsync(cancellationToken);
        var nextProfiles = currentDocument.LocoraProfiles.Profiles.ToList();
        var existingKeys = nextProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Key))
            .Select(profile => profile.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var importedKeys = new List<string>();
        var importedCount = 0;
        var skippedCount = 0;

        foreach (var importedProfile in importedDocument.LocoraProfiles.Profiles)
        {
            var sourceKey = FirstNonWhiteSpace(importedProfile.Key, importedProfile.DisplayName);
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                skippedCount++;
                continue;
            }

            var profileKey = CreateUniqueProfileKey(sourceKey, existingKeys);
            existingKeys.Add(profileKey);
            if (!string.IsNullOrWhiteSpace(importedProfile.Key))
            {
                importedKeyMap[importedProfile.Key] = profileKey;
            }

            if (!string.IsNullOrWhiteSpace(importedProfile.DisplayName))
            {
                importedKeyMap[importedProfile.DisplayName] = profileKey;
            }

            importedKeys.Add(profileKey);
            importedCount++;

            nextProfiles.Add(
                new StackProfileDefinition
                {
                    Key = profileKey,
                    DisplayName = string.IsNullOrWhiteSpace(importedProfile.DisplayName)
                        ? profileKey
                        : importedProfile.DisplayName.Trim(),
                    Description = importedProfile.Description?.Trim() ?? string.Empty,
                    ServiceKeys = NormalizeStringList(importedProfile.ServiceKeys),
                    PackageSelections = NormalizePackageSelections(importedProfile.PackageSelections),
                    Tags = NormalizeStringList(importedProfile.Tags)
                });
        }

        if (importedCount == 0)
        {
            throw new InvalidOperationException($"No stack profiles were found in {resolvedPath}.");
        }

        var activeProfileKey = ResolveImportedActiveProfileKey(
            currentDocument.LocoraProfiles,
            importedDocument.LocoraProfiles.ActiveProfileKey,
            importedKeyMap,
            importedKeys,
            nextProfiles);
        var nextOptions = new StackProfilesOptions
        {
            SchemaVersion = Math.Max(currentDocument.LocoraProfiles.SchemaVersion, importedDocument.LocoraProfiles.SchemaVersion),
            ActiveProfileKey = activeProfileKey,
            Profiles = nextProfiles
        };

        await WriteDocumentAsync(
            new StackProfilesDocument
            {
                LocoraProfiles = nextOptions
            },
            cancellationToken);

        _logger.LogInformation(
            "Imported {ImportedCount} stack profiles from {Path}; skipped {SkippedCount}.",
            importedCount,
            resolvedPath,
            skippedCount);

        return new ImportStackProfilesResult(
            resolvedPath,
            importedCount,
            skippedCount,
            BuildSnapshot(nextOptions, configuredServices));
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
                ? "Loading this profile updates the active profile and merges its package selections into packages.lock.json."
                : "Edit profiles.json or services.json so every service key exists before loading this profile.");
    }

    private static string CreateUniqueProfileKey(string displayName, HashSet<string> existingKeys)
    {
        var baseKey = CreateProfileKey(displayName);
        var profileKey = baseKey;
        var suffix = 2;

        while (existingKeys.Contains(profileKey))
        {
            profileKey = $"{baseKey}-{suffix}";
            suffix++;
        }

        return profileKey;
    }

    private static string CreateProfileKey(string displayName)
    {
        var builder = new StringBuilder();
        var lastWasSeparator = false;

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var key = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "saved-environment" : key;
    }

    private static string FirstNonWhiteSpace(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
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
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> NormalizePackageSelections(IReadOnlyDictionary<string, string>? packageSelections)
    {
        if (packageSelections is null)
        {
            return [];
        }

        return packageSelections
            .Where(selection => !string.IsNullOrWhiteSpace(selection.Key))
            .GroupBy(selection => selection.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(selection => selection.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                selection => selection.Key.Trim(),
                selection => selection.Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveImportedActiveProfileKey(
        StackProfilesOptions currentOptions,
        string importedActiveProfileKey,
        IReadOnlyDictionary<string, string> importedKeyMap,
        IReadOnlyList<string> importedKeys,
        IReadOnlyList<StackProfileDefinition> nextProfiles)
    {
        if (nextProfiles.Any(profile => profile.Key.Equals(currentOptions.ActiveProfileKey, StringComparison.OrdinalIgnoreCase)))
        {
            return currentOptions.ActiveProfileKey;
        }

        if (currentOptions.Profiles.Count == 0 &&
            importedKeyMap.TryGetValue(importedActiveProfileKey, out var mappedImportedActiveProfileKey))
        {
            return mappedImportedActiveProfileKey;
        }

        return currentOptions.Profiles.FirstOrDefault(profile => !string.IsNullOrWhiteSpace(profile.Key))?.Key ??
            importedKeys.FirstOrDefault() ??
            currentOptions.ActiveProfileKey;
    }

    private async Task<StackProfilesDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_paths.ProfilesSettingsFile))
            {
                return new StackProfilesDocument();
            }

            return await ReadDocumentFileAsync(_paths.ProfilesSettingsFile, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read stack profiles from {Path}.", _paths.ProfilesSettingsFile);
            return new StackProfilesDocument();
        }
    }

    private static async Task<StackProfilesDocument> ReadDocumentFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StackProfilesDocument>(stream, ReadSerializerOptions, cancellationToken) ??
            new StackProfilesDocument();
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

    private string ResolveTransferPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Profile import/export path is required.", nameof(path));
        }

        var normalizedPath = path.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.Combine(_paths.ProfilesRoot, normalizedPath));
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

public sealed record SaveActiveEnvironmentProfileResult(
    StackProfileDefinition Profile,
    StackProfileRegistrySnapshot Snapshot);

public sealed record ExportStackProfilesResult(
    string Path,
    int ExportedProfileCount,
    StackProfileRegistrySnapshot Snapshot);

public sealed record ImportStackProfilesResult(
    string Path,
    int ImportedProfileCount,
    int SkippedProfileCount,
    StackProfileRegistrySnapshot Snapshot);
