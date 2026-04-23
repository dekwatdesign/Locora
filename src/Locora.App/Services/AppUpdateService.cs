using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.App.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IEnvironmentPaths _environmentPaths;
    private readonly AppUpdateSettings _settings;
    private readonly ILogger<AppUpdateService> _logger;
    private readonly HttpClient _httpClient = new();

    public AppUpdateService(
        IEnvironmentPaths environmentPaths,
        IOptions<AppSettings> settings,
        ILogger<AppUpdateService> logger)
    {
        _environmentPaths = environmentPaths;
        _settings = settings.Value.Updates;
        _logger = logger;
    }

    public AppUpdateStatus GetCurrentStatus()
    {
        EnsureUpdateWorkspace();

        var currentVersion = ResolveCurrentVersion();
        var latestVersion = string.Empty;
        var releasePageUri = _settings.ReleasePageUri;
        var downloadUri = string.Empty;
        var sha256 = string.Empty;
        var checkedAt = DateTimeOffset.Now;

        if (File.Exists(_environmentPaths.AppUpdatePlanFile))
        {
            try
            {
                var plan = JsonSerializer.Deserialize<AppUpdatePlan>(
                    File.ReadAllText(_environmentPaths.AppUpdatePlanFile),
                    SerializerOptions);
                if (plan is not null)
                {
                    latestVersion = plan.LatestVersion ?? string.Empty;
                    releasePageUri = string.IsNullOrWhiteSpace(plan.ReleasePageUri) ? releasePageUri : plan.ReleasePageUri;
                    downloadUri = plan.DownloadUri ?? string.Empty;
                    sha256 = plan.Sha256 ?? string.Empty;
                    checkedAt = plan.CheckedAt;
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "App update plan could not be read.");
            }
        }

        if (string.IsNullOrWhiteSpace(_settings.ManifestUri))
        {
            return CreateStatus(
                "Not configured",
                "App update checks need a release manifest.",
                "Set Locora:Updates:ManifestUri in appsettings.json to an HTTPS URL or local JSON file. Locora will compare that manifest with the running app version and write an update plan under usr/updates.",
                currentVersion,
                latestVersion,
                releasePageUri,
                downloadUri,
                sha256,
                checkedAt,
                isUpdateAvailable: false);
        }

        return CreateStatus(
            string.IsNullOrWhiteSpace(latestVersion) ? "Ready" : "Last checked",
            string.IsNullOrWhiteSpace(latestVersion) ? "Update manifest is configured." : $"Last checked version {latestVersion}.",
            "Run Check for Updates to refresh the manifest cache and update plan.",
            currentVersion,
            latestVersion,
            releasePageUri,
            downloadUri,
            sha256,
            checkedAt,
            isUpdateAvailable: false);
    }

    public async Task<AppUpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureUpdateWorkspace();

        var currentVersion = ResolveCurrentVersion();
        var checkedAt = DateTimeOffset.Now;

        if (string.IsNullOrWhiteSpace(_settings.ManifestUri))
        {
            var notConfigured = CreateStatus(
                "Not configured",
                "App update checks need a release manifest.",
                "Set Locora:Updates:ManifestUri in appsettings.json to an HTTPS URL or local JSON file, then run Check for Updates again.",
                currentVersion,
                latestVersion: string.Empty,
                _settings.ReleasePageUri,
                downloadUri: string.Empty,
                sha256: string.Empty,
                checkedAt,
                isUpdateAvailable: false);
            WritePlan(notConfigured, null);
            return notConfigured;
        }

        try
        {
            var manifestJson = await LoadManifestJsonAsync(_settings.ManifestUri, cancellationToken);
            await File.WriteAllTextAsync(_environmentPaths.AppUpdateManifestCacheFile, manifestJson, Encoding.UTF8, cancellationToken);

            var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(manifestJson, SerializerOptions)
                ?? throw new InvalidOperationException("Release manifest is empty or invalid.");

            var latestVersion = NormalizeVersionLabel(manifest.Version);
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                throw new InvalidOperationException("Release manifest does not include a version.");
            }

            var releasePageUri = FirstNonBlank(manifest.ReleasePageUri, _settings.ReleasePageUri);
            var downloadUri = FirstNonBlank(SelectDownloadUri(manifest), manifest.DownloadUri);
            var sha256 = FirstNonBlank(SelectSha256(manifest), manifest.Sha256);
            var isPrerelease = latestVersion.Contains('-', StringComparison.Ordinal);
            var isUpdateAvailable = IsVersionNewer(latestVersion, currentVersion);
            string state;
            string summary;
            string details;

            if (isPrerelease && !_settings.AllowPrerelease)
            {
                state = "Prerelease skipped";
                summary = $"Latest manifest version {latestVersion} is prerelease.";
                details = "Set Locora:Updates:AllowPrerelease to true to include prerelease builds in update checks.";
                isUpdateAvailable = false;
            }
            else if (isUpdateAvailable)
            {
                state = "Update available";
                summary = $"Locora {latestVersion} is available. Current version is {currentVersion}.";
                details = BuildUpdateDetails(manifest, releasePageUri, downloadUri);
            }
            else
            {
                state = "Up to date";
                summary = $"Current version {currentVersion} is up to date for channel {ResolveChannel(manifest)}.";
                details = BuildUpdateDetails(manifest, releasePageUri, downloadUri);
            }

            var status = CreateStatus(
                state,
                summary,
                details,
                currentVersion,
                latestVersion,
                releasePageUri,
                downloadUri,
                sha256,
                checkedAt,
                isUpdateAvailable);
            WritePlan(status, manifest);
            return status;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "App update check failed.");
            var status = CreateStatus(
                "Check failed",
                $"App update check failed: {exception.Message}",
                "Verify Locora:Updates:ManifestUri points to a reachable HTTPS URL or local JSON file.",
                currentVersion,
                latestVersion: string.Empty,
                _settings.ReleasePageUri,
                downloadUri: string.Empty,
                sha256: string.Empty,
                checkedAt,
                isUpdateAvailable: false);
            WritePlan(status, null);
            return status;
        }
    }

    private AppUpdateStatus CreateStatus(
        string state,
        string summary,
        string details,
        string currentVersion,
        string latestVersion,
        string releasePageUri,
        string downloadUri,
        string sha256,
        DateTimeOffset checkedAt,
        bool isUpdateAvailable)
    {
        return new AppUpdateStatus(
            state,
            summary,
            details,
            currentVersion,
            latestVersion,
            _settings.Channel,
            _settings.ManifestUri,
            releasePageUri,
            downloadUri,
            sha256,
            _environmentPaths.AppUpdateManifestCacheFile,
            _environmentPaths.AppUpdatePlanFile,
            _environmentPaths.AppUpdateManifestExampleFile,
            checkedAt,
            isUpdateAvailable);
    }

    private async Task<string> LoadManifestJsonAsync(string manifestUri, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(manifestUri, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, _settings.CheckTimeoutMs)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            return await _httpClient.GetStringAsync(uri, linked.Token);
        }

        var manifestPath = ResolveManifestPath(manifestUri);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Release manifest file does not exist: {manifestPath}", manifestPath);
        }

        return await File.ReadAllTextAsync(manifestPath, cancellationToken);
    }

    private string ResolveManifestPath(string manifestUri)
    {
        if (Path.IsPathFullyQualified(manifestUri))
        {
            return Path.GetFullPath(manifestUri);
        }

        return Path.GetFullPath(Path.Combine(_environmentPaths.AppRoot, manifestUri));
    }

    private void EnsureUpdateWorkspace()
    {
        Directory.CreateDirectory(_environmentPaths.AppUpdateRoot);
        Directory.CreateDirectory(_environmentPaths.AppUpdateDownloadRoot);

        if (!File.Exists(_environmentPaths.AppUpdateManifestExampleFile))
        {
            File.WriteAllText(_environmentPaths.AppUpdateManifestExampleFile, BuildExampleManifest(), Encoding.UTF8);
        }
    }

    private void WritePlan(AppUpdateStatus status, AppUpdateManifest? manifest)
    {
        var plan = new AppUpdatePlan(
            status.CheckedAt,
            status.State,
            status.Summary,
            status.Details,
            status.CurrentVersion,
            status.LatestVersion,
            status.Channel,
            status.ManifestUri,
            status.ReleasePageUri,
            status.DownloadUri,
            status.Sha256,
            status.ManifestCachePath,
            status.IsUpdateAvailable,
            manifest?.MinimumSupportedVersion ?? string.Empty,
            manifest?.ReleaseDate ?? string.Empty,
            manifest?.ReleaseNotes ?? string.Empty);
        File.WriteAllText(_environmentPaths.AppUpdatePlanFile, JsonSerializer.Serialize(plan, SerializerOptions), Encoding.UTF8);
    }

    private string BuildUpdateDetails(AppUpdateManifest manifest, string releasePageUri, string downloadUri)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.ReleaseDate))
        {
            parts.Add($"Release date: {manifest.ReleaseDate}.");
        }

        if (!string.IsNullOrWhiteSpace(releasePageUri))
        {
            parts.Add($"Release page: {releasePageUri}.");
        }

        if (!string.IsNullOrWhiteSpace(downloadUri))
        {
            parts.Add($"Download: {downloadUri}.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinimumSupportedVersion))
        {
            parts.Add($"Minimum supported version: {manifest.MinimumSupportedVersion}.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            parts.Add(manifest.ReleaseNotes);
        }

        return parts.Count == 0
            ? "The release manifest was read and the update plan was refreshed."
            : string.Join(" ", parts);
    }

    private string ResolveChannel(AppUpdateManifest manifest)
    {
        return FirstNonBlank(manifest.Channel, _settings.Channel, "stable");
    }

    private static string SelectDownloadUri(AppUpdateManifest manifest)
    {
        return manifest.Assets?
            .FirstOrDefault(asset => string.Equals(asset.Platform, "windows", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(asset.Architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(asset.Architecture)))?
            .DownloadUri ?? string.Empty;
    }

    private static string SelectSha256(AppUpdateManifest manifest)
    {
        return manifest.Assets?
            .FirstOrDefault(asset => string.Equals(asset.Platform, "windows", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(asset.Architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(asset.Architecture)))?
            .Sha256 ?? string.Empty;
    }

    private static bool IsVersionNewer(string latestVersion, string currentVersion)
    {
        var latest = ParseComparableVersion(latestVersion);
        var current = ParseComparableVersion(currentVersion);
        if (latest is null || current is null)
        {
            return !latestVersion.Equals(currentVersion, StringComparison.OrdinalIgnoreCase);
        }

        return latest > current;
    }

    private static Version? ParseComparableVersion(string value)
    {
        var normalized = NormalizeVersionLabel(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex > 0)
        {
            normalized = normalized[..suffixIndex];
        }

        if (normalized.Count(character => character == '.') == 0)
        {
            normalized += ".0";
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static string NormalizeVersionLabel(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.StartsWith('v') || normalized.StartsWith('V')
            ? normalized[1..]
            : normalized;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(AppUpdateService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return NormalizeVersionLabel(informationalVersion);
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private string BuildExampleManifest()
    {
        var example = new AppUpdateManifest(
            Schema: "locora.app-update.v1",
            AppId: "locora",
            Channel: _settings.Channel,
            Version: ResolveCurrentVersion(),
            ReleaseDate: DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            ReleaseNotes: "Describe notable fixes, migration notes, and manual install steps for this release.",
            ReleasePageUri: "https://example.com/locora/releases",
            DownloadUri: "https://example.com/locora/Locora-win-x64.zip",
            Sha256: "replace-with-lowercase-sha256",
            MinimumSupportedVersion: "0.1.0",
            RequiresManualInstall: true,
            Assets:
            [
                new AppUpdateAsset(
                    Platform: "windows",
                    Architecture: "x64",
                    DownloadUri: "https://example.com/locora/Locora-win-x64.zip",
                    Sha256: "replace-with-lowercase-sha256")
            ]);

        return JsonSerializer.Serialize(example, SerializerOptions);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private sealed record AppUpdateManifest(
        string Schema,
        string AppId,
        string Channel,
        string Version,
        string ReleaseDate,
        string ReleaseNotes,
        string ReleasePageUri,
        string DownloadUri,
        string Sha256,
        string MinimumSupportedVersion,
        bool RequiresManualInstall,
        IReadOnlyList<AppUpdateAsset>? Assets);

    private sealed record AppUpdateAsset(
        string Platform,
        string Architecture,
        string DownloadUri,
        string Sha256);

    private sealed record AppUpdatePlan(
        DateTimeOffset CheckedAt,
        string State,
        string Summary,
        string Details,
        string CurrentVersion,
        string LatestVersion,
        string Channel,
        string ManifestUri,
        string ReleasePageUri,
        string DownloadUri,
        string Sha256,
        string ManifestCachePath,
        bool IsUpdateAvailable,
        string MinimumSupportedVersion,
        string ReleaseDate,
        string ReleaseNotes);
}
