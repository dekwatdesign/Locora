using Locora.Domain.Enums;

namespace Locora.Domain.Entities;

public sealed record HealthIssue(
    HealthIssueSeverity Severity,
    string Title,
    string Description,
    string SuggestedAction);
