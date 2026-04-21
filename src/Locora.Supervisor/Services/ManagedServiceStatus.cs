namespace Locora.Supervisor.Services;

public sealed record ManagedServiceStatus(
    string Key,
    string DisplayName,
    string Version,
    int? Port,
    string State,
    bool AutoStart,
    string? Note,
    bool ExecutableExists,
    bool PortResponsive,
    int? ProcessId,
    string ExecutablePath);
