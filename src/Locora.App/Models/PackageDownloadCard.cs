using System.Windows.Input;

namespace Locora.App.Models;

public sealed record PackageDownloadCard(
    string Id,
    string Name,
    string StateLabel,
    string ChecksumLabel,
    string ExtractionLabel,
    string VersionLabel,
    string SourceLabel,
    string ArtifactLabel,
    string CachePath,
    string InstallPath,
    string ActivePath,
    string Summary,
    string Details,
    bool IsCached,
    bool IsExtracted,
    bool CanRemoveInstall,
    ICommand RemoveInstallCommand)
{
    public string RemoveAutomationName => $"Remove installed versions for {Name}";
}
