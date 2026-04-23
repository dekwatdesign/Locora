namespace Locora.App.Services;

public sealed record AppUpdateStatus(
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
    string PlanPath,
    string ManifestExamplePath,
    DateTimeOffset CheckedAt,
    bool IsUpdateAvailable);
