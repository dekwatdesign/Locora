using System.Text.Json;

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

    public List<ProjectOverrideDefinition> ProjectOverrides { get; init; } = [];
}

public sealed class ProjectOverrideDefinition
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Domain { get; init; } = string.Empty;

    public string Scheme { get; init; } = string.Empty;

    public string DocumentRoot { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public string Framework { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonElement Tags { get; init; }
}
