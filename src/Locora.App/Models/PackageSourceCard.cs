namespace Locora.App.Models;

public sealed record PackageSourceCard(
    string Id,
    string Name,
    string StateLabel,
    string SourceLabel,
    string PackageCountLabel,
    string ManifestPath,
    string Summary,
    string Details,
    bool IsEnabled);
