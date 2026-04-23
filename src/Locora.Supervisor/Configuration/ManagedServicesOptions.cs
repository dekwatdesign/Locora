namespace Locora.Supervisor.Configuration;

public sealed class ManagedServicesOptions
{
    public const string SectionName = "LocoraServices";

    public List<ManagedServiceDefinition> Services { get; init; } = [];

    public List<ServicePresetDefinition> Presets { get; init; } = [];
}

public sealed class ServicePresetDefinition
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public List<string> ServiceKeys { get; init; } = [];

    public List<string> Tags { get; init; } = [];
}

public sealed class ManagedServiceDefinition
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Kind { get; init; } = "generic";

    public string Version { get; init; } = "unknown";

    public string? VersionResolutionNote { get; init; }

    public string RelativeExecutablePath { get; init; } = string.Empty;

    public string? RelativeStopExecutablePath { get; init; }

    public string? RelativeWorkingDirectory { get; init; }

    public List<string> Arguments { get; init; } = [];

    public List<string> StopArguments { get; init; } = [];

    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];

    public int? Port { get; init; }

    public bool AutoStart { get; init; }

    public bool RestartOnCrash { get; init; } = true;

    public int RestartBackoffMs { get; init; } = 2000;

    public int MaxRestartAttempts { get; init; } = 3;

    public int RestartWindowMs { get; init; } = 60000;

    public int StartTimeoutMs { get; init; } = 5000;

    public int StopTimeoutMs { get; init; } = 3000;
}
