namespace Locora.Supervisor.Configuration;

public sealed class ProjectDiscoveryOptions
{
    public const string SectionName = "LocoraProjects";

    public bool EnableAutoDiscovery { get; init; } = true;

    public bool GenerateNginxVHosts { get; init; } = true;

    public bool GenerateApacheVHosts { get; init; } = true;

    public bool GenerateHostsPreview { get; init; } = true;

    public string DomainSuffix { get; init; } = "locora.test";

    public string DefaultScheme { get; init; } = "http";

    public List<string> IndexFileNames { get; init; } = ["index.php", "index.html", "index.htm"];

    public List<string> IgnoredDirectoryNames { get; init; } = [".git", ".idea", ".vscode", "node_modules", "vendor"];
}
