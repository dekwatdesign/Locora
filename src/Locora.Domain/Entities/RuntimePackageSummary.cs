namespace Locora.Domain.Entities;

public sealed record RuntimePackageSummary(
    int RuntimeCount,
    int InstalledCount,
    int ActiveCount,
    int SwitchableCount,
    int AttentionCount,
    string Summary,
    string Details);
