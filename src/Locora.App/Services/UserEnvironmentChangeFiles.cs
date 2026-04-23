namespace Locora.App.Services;

public sealed record UserEnvironmentChangeFiles(
    string RootPath,
    string ApplyScriptPath,
    string RemoveScriptPath,
    string ManifestPath,
    string BackupRootPath,
    IReadOnlyList<string> ManagedPathEntries,
    IReadOnlyDictionary<string, string> ManagedVariables);
