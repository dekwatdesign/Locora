namespace Locora.Infrastructure.Configuration;

public sealed class AppSettings
{
    public const string SectionName = "Locora";

    public DirectorySettings Directories { get; init; } = new();

    public SupervisorConnectionSettings Supervisor { get; init; } = new();

    public ExperienceSettings Experience { get; init; } = new();
}

public sealed class DirectorySettings
{
    public string UserRootName { get; init; } = "usr";

    public string ProjectRootName { get; init; } = "www";

    public string BinRootName { get; init; } = "bin";

    public string DataRootName { get; init; } = "data";

    public string TempRootName { get; init; } = "temp";
}

public sealed class SupervisorConnectionSettings
{
    public string PipeName { get; init; } = "locora-supervisor";

    public int ConnectTimeoutMs { get; init; } = 1500;
}

public sealed class ExperienceSettings
{
    public string PreferredWebServer { get; init; } = "Nginx";

    public string PreferredDatabase { get; init; } = "MariaDB";

    public string PreferredShell { get; init; } = "PowerShell";

    public string PreferredEditor { get; init; } = "VS Code";
}
