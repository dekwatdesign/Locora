namespace Locora.App.Models;

public sealed record PortDiagnosticCard(
    string Key,
    string Name,
    string PortLabel,
    string StateLabel,
    string Summary,
    string Details,
    string SuggestedAction,
    string OwnerLabel);
