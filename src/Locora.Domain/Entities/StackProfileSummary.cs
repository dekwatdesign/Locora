namespace Locora.Domain.Entities;

public sealed record StackProfileSummary(
    int ProfileCount,
    int ValidProfileCount,
    int InvalidProfileCount,
    string ActiveProfileKey,
    string ActiveProfileName,
    string Summary,
    string Details);
