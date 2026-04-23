namespace Locora.App.Models;

public sealed record ToolPackageCard(
    string Id,
    string Name,
    string Family,
    string StateLabel,
    string VersionLabel,
    string CommandsLabel,
    string SourceLabel,
    string PathLabel,
    string Summary,
    string Details,
    bool IsSelected,
    bool IsInstalled,
    bool IsActive);
