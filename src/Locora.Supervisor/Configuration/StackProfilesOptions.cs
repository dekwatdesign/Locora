namespace Locora.Supervisor.Configuration;

public sealed class StackProfilesDocument
{
    public StackProfilesOptions LocoraProfiles { get; init; } = new();
}

public sealed class StackProfilesOptions
{
    public const string SectionName = "LocoraProfiles";

    public int SchemaVersion { get; init; } = 1;

    public string ActiveProfileKey { get; init; } = "full-stack";

    public List<StackProfileDefinition> Profiles { get; init; } = [];
}

public sealed class StackProfileDefinition
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public List<string> ServiceKeys { get; init; } = [];

    public Dictionary<string, string> PackageSelections { get; init; } = [];

    public List<string> Tags { get; init; } = [];
}
