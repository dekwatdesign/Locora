using Locora.Domain.Enums;

namespace Locora.Domain.Entities;

public sealed record PortDiagnostic(
    string Key,
    string DisplayName,
    int Port,
    PortDiagnosticState State,
    string Summary,
    string Details,
    string SuggestedAction,
    int? OwnerProcessId,
    string? OwnerProcessName);
