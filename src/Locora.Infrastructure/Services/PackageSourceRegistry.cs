using System.Text.Json;
using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Infrastructure.Services;

public sealed class PackageSourceRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IEnvironmentPaths _paths;
    private readonly IOptionsMonitor<PackageSourcesOptions> _options;
    private readonly ILogger<PackageSourceRegistry> _logger;

    public PackageSourceRegistry(
        IEnvironmentPaths paths,
        IOptionsMonitor<PackageSourcesOptions> options,
        ILogger<PackageSourceRegistry> logger)
    {
        _paths = paths;
        _options = options;
        _logger = logger;
    }

    public async Task<PackageSourceRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return (await GetCatalogAsync(cancellationToken)).Snapshot;
    }

    internal async Task<PackageCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var configuredSources = _options.CurrentValue.Sources
            .OrderBy(source => source.Priority)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = new List<PackageCatalogSource>(configuredSources.Length);
        var catalogPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalVersionCount = 0;

        foreach (var source in configuredSources)
        {
            PackageSourceLoadResult result;

            if (string.IsNullOrWhiteSpace(source.Id))
            {
                result = CreateErrorResult(source, "Source id is required.", "Set a unique `Id` in usr/config/sources.json.");
            }
            else if (!seenSourceIds.Add(source.Id))
            {
                result = CreateErrorResult(source, $"Duplicate source id '{source.Id}'.", "Rename one of the duplicate source ids so registry priority and selections are deterministic.");
            }
            else
            {
                result = await LoadSourceAsync(source, cancellationToken);
            }

            sources.Add(new PackageCatalogSource(source, result.Status, result.Manifest));

            if (result.Manifest is null)
            {
                continue;
            }

            foreach (var package in result.Manifest.Packages.Where(package => !string.IsNullOrWhiteSpace(package.PackageId)))
            {
                catalogPackages.Add(package.PackageId);
                totalVersionCount += package.Versions.Count;
            }
        }

        var statuses = sources.Select(source => source.Status).ToArray();
        var enabledSourceCount = statuses.Count(source => source.IsEnabled);
        var readySourceCount = statuses.Count(source => source.State.Equals("Ready", StringComparison.OrdinalIgnoreCase));
        var errorSourceCount = statuses.Count(source => source.State.Equals("Error", StringComparison.OrdinalIgnoreCase));

        return new PackageCatalogSnapshot(
            new PackageSourceRegistrySnapshot(
                statuses,
                enabledSourceCount,
                readySourceCount,
                errorSourceCount,
                catalogPackages.Count,
                totalVersionCount,
                BuildSummary(enabledSourceCount, readySourceCount, errorSourceCount, catalogPackages.Count, totalVersionCount),
                BuildDetails(statuses, enabledSourceCount, readySourceCount, errorSourceCount)),
            sources);
    }

    private async Task<PackageSourceLoadResult> LoadSourceAsync(PackageSourceDefinition source, CancellationToken cancellationToken)
    {
        var manifestPath = ResolvePath(source.ManifestPath);

        if (!source.IsEnabled)
        {
            return new PackageSourceLoadResult(
                new PackageSourceStatusSnapshot(
                    source.Id,
                    ResolveDisplayName(source),
                    source.Kind,
                    "Disabled",
                    source.Channel,
                    source.Priority,
                    manifestPath,
                    0,
                    0,
                    "Source disabled",
                    "This source is configured but disabled in usr/config/sources.json.",
                    false),
                null);
        }

        if (!source.Kind.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return CreateErrorResult(
                source,
                $"Source kind '{source.Kind}' is not supported yet.",
                "Use a bundled or local file manifest for now. Remote manifest sources are not supported yet.");
        }

        if (string.IsNullOrWhiteSpace(source.ManifestPath))
        {
            return CreateErrorResult(
                source,
                "Manifest path is missing.",
                "Set `ManifestPath` to a bundled or portable JSON manifest.");
        }

        if (!File.Exists(manifestPath))
        {
            return CreateErrorResult(
                source,
                "Manifest file could not be found.",
                $"Expected manifest: {manifestPath}");
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(stream, SerializerOptions, cancellationToken);

            if (manifest is null)
            {
                return CreateErrorResult(source, "Manifest file was empty.", $"Expected schema `{PackageManifest.SchemaName}`.");
            }

            if (!string.Equals(manifest.Schema, PackageManifest.SchemaName, StringComparison.OrdinalIgnoreCase))
            {
                return CreateErrorResult(
                    source,
                    $"Manifest schema '{manifest.Schema}' is not supported.",
                    $"Expected schema `{PackageManifest.SchemaName}`.");
            }

            var packageCount = manifest.Packages.Count;
            var versionCount = manifest.Packages.Sum(package => package.Versions.Count);

            _logger.LogInformation(
                "Loaded package source {SourceId} with manifest {ManifestId}: {PackageCount} packages / {VersionCount} versions",
                source.Id,
                manifest.ManifestId,
                packageCount,
                versionCount);

            return new PackageSourceLoadResult(
                new PackageSourceStatusSnapshot(
                    source.Id,
                    ResolveDisplayName(source),
                    source.Kind,
                    "Ready",
                    manifest.Channel,
                    source.Priority,
                    manifestPath,
                    packageCount,
                    versionCount,
                    $"Loaded manifest {manifest.ManifestId}",
                    $"Platform {manifest.Platform}/{manifest.Architecture}. {packageCount} packages and {versionCount} versions are available from this source.",
                    true),
                manifest);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Failed to parse package manifest for source {SourceId}", source.Id);
            return CreateErrorResult(
                source,
                "Manifest JSON could not be parsed.",
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load package source {SourceId}", source.Id);
            return CreateErrorResult(
                source,
                "Manifest load failed.",
                exception.Message);
        }
    }

    private PackageSourceLoadResult CreateErrorResult(PackageSourceDefinition source, string summary, string details)
    {
        return new PackageSourceLoadResult(
            new PackageSourceStatusSnapshot(
                string.IsNullOrWhiteSpace(source.Id) ? "(missing-id)" : source.Id,
                ResolveDisplayName(source),
                source.Kind,
                "Error",
                source.Channel,
                source.Priority,
                ResolvePath(source.ManifestPath),
                0,
                0,
                summary,
                details,
                source.IsEnabled),
            null);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(unset)";
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, path));
    }

    private static string ResolveDisplayName(PackageSourceDefinition source)
    {
        return string.IsNullOrWhiteSpace(source.DisplayName)
            ? (string.IsNullOrWhiteSpace(source.Id) ? "Unnamed package source" : source.Id)
            : source.DisplayName;
    }

    private static string BuildSummary(
        int enabledSourceCount,
        int readySourceCount,
        int errorSourceCount,
        int packageCount,
        int versionCount)
    {
        if (enabledSourceCount == 0)
        {
            return "No package sources are enabled.";
        }

        if (readySourceCount == 0)
        {
            return errorSourceCount > 0
                ? "Package sources need attention before runtimes can be cataloged."
                : "No package manifests loaded yet.";
        }

        return $"{readySourceCount}/{enabledSourceCount} package sources ready, {packageCount} packages / {versionCount} versions cataloged.";
    }

    private static string BuildDetails(
        IReadOnlyList<PackageSourceStatusSnapshot> statuses,
        int enabledSourceCount,
        int readySourceCount,
        int errorSourceCount)
    {
        if (statuses.Count == 0)
        {
            return "Add a source entry to usr/config/sources.json to start cataloging runtimes and tools.";
        }

        if (enabledSourceCount == 0)
        {
            return "Enable at least one source in usr/config/sources.json so Locora can resolve package manifests.";
        }

        if (errorSourceCount > 0)
        {
            var failingSources = statuses
                .Where(source => source.State.Equals("Error", StringComparison.OrdinalIgnoreCase))
                .Select(source => source.DisplayName)
                .Take(3);

            return $"Resolve failing source entries first: {string.Join(", ", failingSources)}.";
        }

        if (readySourceCount > 0)
        {
            var leadSource = statuses
                .Where(source => source.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                .OrderBy(source => source.Priority)
                .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return leadSource is null
                ? "Package source registry is ready."
                : $"Highest-priority ready source: {leadSource.DisplayName} (priority {leadSource.Priority}).";
        }

        return "Package source registry is waiting for a readable manifest.";
    }
}

public sealed record PackageSourceRegistrySnapshot(
    IReadOnlyList<PackageSourceStatusSnapshot> Sources,
    int EnabledSourceCount,
    int ReadySourceCount,
    int ErrorSourceCount,
    int PackageCount,
    int VersionCount,
    string Summary,
    string Details);

internal sealed record PackageCatalogSnapshot(
    PackageSourceRegistrySnapshot Snapshot,
    IReadOnlyList<PackageCatalogSource> Sources);

internal sealed record PackageCatalogSource(
    PackageSourceDefinition Definition,
    PackageSourceStatusSnapshot Status,
    PackageManifest? Manifest);

public sealed record PackageSourceStatusSnapshot(
    string Id,
    string DisplayName,
    string Kind,
    string State,
    string Channel,
    int Priority,
    string ManifestPath,
    int PackageCount,
    int VersionCount,
    string Summary,
    string Details,
    bool IsEnabled);

internal sealed record PackageSourceLoadResult(
    PackageSourceStatusSnapshot Status,
    PackageManifest? Manifest);
