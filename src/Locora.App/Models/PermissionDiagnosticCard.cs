namespace Locora.App.Models;

public sealed record PermissionDiagnosticCard(
    string Key,
    string Name,
    string StateLabel,
    string Summary,
    string Details,
    string SuggestedAction);
