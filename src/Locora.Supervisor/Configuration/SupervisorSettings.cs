namespace Locora.Supervisor.Configuration;

public sealed class SupervisorSettings
{
    public const string SectionName = "LocoraSupervisor";

    public IReadOnlyList<string> AutoStartServices { get; init; } = new List<string> { "nginx", "mariadb" };

    public int ProbeIntervalMs { get; init; } = 3000;
}
