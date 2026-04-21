namespace Locora.App.Models;

public sealed record HealthIssueCard(
    string Severity,
    string Title,
    string Description,
    string SuggestedAction);
