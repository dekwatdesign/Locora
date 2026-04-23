namespace Locora.Domain.Entities;

public sealed record StackProfileStatus(
    string Key,
    string DisplayName,
    string Description,
    string State,
    IReadOnlyList<string> ServiceKeys,
    IReadOnlyDictionary<string, string> PackageSelections,
    IReadOnlyList<string> Tags,
    bool IsActive,
    bool IsValid,
    string Summary,
    string Details);
