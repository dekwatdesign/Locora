namespace Locora.Domain.Entities;

public sealed record RuntimePackageStatus(
    string PackageId,
    string DisplayName,
    string Family,
    string Kind,
    string State,
    string RequestedVersion,
    string ResolvedVersion,
    string ActiveVersion,
    string DefaultVersion,
    string SourceId,
    string Channel,
    string InstallRootPath,
    string ActivePath,
    string ExecutablePath,
    string Summary,
    string Details,
    IReadOnlyList<string> AvailableVersions,
    IReadOnlyList<string> InstalledVersions,
    bool SupportsSwitching,
    bool IsInstalled,
    bool IsActive);
