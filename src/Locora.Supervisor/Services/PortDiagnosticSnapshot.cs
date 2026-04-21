namespace Locora.Supervisor.Services;

public sealed record PortDiagnosticSnapshot(
    string Key,
    string DisplayName,
    int Port,
    string State,
    string Summary,
    string Details,
    string SuggestedAction,
    int? OwnerProcessId,
    string? OwnerProcessName);
