namespace Locora.Domain.Entities;

public sealed record ServicePresetStatus(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> ServiceKeys,
    IReadOnlyList<string> Tags,
    bool IsValid,
    string State,
    string ServicesLabel,
    string Summary,
    string Details);
