namespace Locora.Domain.Entities;

public sealed record ValidationResult(
    string Key,
    string DisplayName,
    bool IsValid,
    string Summary,
    string Details,
    DateTimeOffset CheckedAt);
