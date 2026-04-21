namespace Locora.Supervisor.Services;

public sealed record ConfigValidationResult(
    string Key,
    string DisplayName,
    bool IsValid,
    string Summary,
    string Details,
    DateTimeOffset CheckedAt);
