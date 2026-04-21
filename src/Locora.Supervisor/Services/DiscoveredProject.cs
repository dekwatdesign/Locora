namespace Locora.Supervisor.Services;

public sealed record DiscoveredProject(
    string Name,
    string Slug,
    string Path,
    string DocumentRoot,
    string Url,
    string Runtime,
    string Framework,
    bool UsesHttps);
