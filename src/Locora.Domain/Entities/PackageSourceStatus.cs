namespace Locora.Domain.Entities;

public sealed record PackageSourceStatus(
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
