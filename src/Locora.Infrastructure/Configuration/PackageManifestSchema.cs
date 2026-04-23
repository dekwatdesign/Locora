namespace Locora.Infrastructure.Configuration;

public sealed class PackageSourcesDocument
{
    public PackageSourcesOptions LocoraPackageSources { get; init; } = new();
}

public sealed class PackageSourcesOptions
{
    public const string SectionName = "LocoraPackageSources";

    public int SchemaVersion { get; init; } = 1;

    public List<PackageSourceDefinition> Sources { get; init; } = [];
}

public sealed class PackageSourceDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Kind { get; init; } = "file";

    public string Channel { get; init; } = "stable";

    public string ManifestPath { get; init; } = string.Empty;

    public int Priority { get; init; } = 100;

    public bool IsEnabled { get; init; } = true;

    public bool IsBundled { get; init; } = false;
}

public sealed class PackagesLockOptions
{
    public const string SectionName = "LocoraPackagesLock";

    public int SchemaVersion { get; init; } = 1;

    public List<PackageSelectionRecord> ActiveSelections { get; init; } = [];

    public List<InstalledPackageRecord> InstalledPackages { get; init; } = [];
}

public sealed class PackagesLockDocument
{
    public PackagesLockOptions LocoraPackagesLock { get; init; } = new();
}

public sealed class PackageSelectionRecord
{
    public string PackageId { get; init; } = string.Empty;

    public string RequestedVersion { get; init; } = string.Empty;

    public string SelectionStrategy { get; init; } = "latest-matching";

    public string SourceId { get; init; } = string.Empty;
}

public sealed class InstalledPackageRecord
{
    public string PackageId { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string InstallPath { get; init; } = string.Empty;

    public string Status { get; init; } = "installed";

    public DateTimeOffset? InstalledAtUtc { get; init; }

    public string? Sha256 { get; init; }
}

public sealed class PackageManifest
{
    public const string SchemaName = "locora.package-manifest.v1";

    public string Schema { get; init; } = SchemaName;

    public string ManifestId { get; init; } = "locora.windows-x64";

    public string DisplayName { get; init; } = string.Empty;

    public string Channel { get; init; } = "stable";

    public string Platform { get; init; } = "windows";

    public string Architecture { get; init; } = "x64";

    public List<PackageManifestPackage> Packages { get; init; } = [];
}

public sealed class PackageManifestPackage
{
    public string PackageId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Kind { get; init; } = "runtime";

    public string Family { get; init; } = string.Empty;

    public string InstallRootPath { get; init; } = string.Empty;

    public string? ActiveAliasPath { get; init; }

    public string DefaultVersion { get; init; } = string.Empty;

    public bool SupportsSideBySideInstall { get; init; } = true;

    public bool SupportsActiveAlias { get; init; } = true;

    public List<string> Tags { get; init; } = [];

    public string? Description { get; init; }

    public List<PackageManifestVersion> Versions { get; init; } = [];
}

public sealed class PackageManifestVersion
{
    public string Version { get; init; } = string.Empty;

    public string Channel { get; init; } = "stable";

    public string Architecture { get; init; } = "x64";

    public string ArchiveType { get; init; } = "zip";

    public string? ArtifactPath { get; init; }

    public string? DownloadUri { get; init; }

    public string RelativeInstallPath { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public string? StopExecutablePath { get; init; }

    public string? WorkingDirectoryPath { get; init; }

    public List<string> ProvidesCommands { get; init; } = [];

    public List<string> Dependencies { get; init; } = [];

    public string? Sha256 { get; init; }
}
