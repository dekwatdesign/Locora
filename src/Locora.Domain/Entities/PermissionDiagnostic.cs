using Locora.Domain.Enums;

namespace Locora.Domain.Entities;

public sealed record PermissionDiagnostic(
    string Key,
    string DisplayName,
    PermissionDiagnosticState State,
    string Summary,
    string Details,
    string SuggestedAction);
