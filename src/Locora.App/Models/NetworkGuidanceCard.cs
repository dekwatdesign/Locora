namespace Locora.App.Models;

public sealed record NetworkGuidanceCard(
    string Key,
    string Title,
    string ScopeLabel,
    string PortLabel,
    string Summary,
    string Details,
    string SuggestedAction,
    string SeverityLabel)
{
    public string AutomationName => $"Network guidance: {Title}";

    public string SearchText => $"{Key} {Title} {ScopeLabel} {PortLabel} {Summary} {Details} {SuggestedAction} {SeverityLabel}";
}
