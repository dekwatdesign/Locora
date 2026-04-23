namespace Locora.App.Services;

public sealed record PortableDistributionFiles(
    string RootPath,
    string ArtifactsRootPath,
    string ScriptPath,
    string PlanPath,
    string ReadmePath,
    string ManifestTemplatePath,
    string Configuration,
    string RuntimeIdentifier,
    bool IncludeRuntimeBinaries,
    bool IncludePackageCache,
    bool IncludeUserData,
    bool CreateReleaseManifest);
