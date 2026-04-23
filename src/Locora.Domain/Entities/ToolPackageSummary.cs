namespace Locora.Domain.Entities;

public sealed record ToolPackageSummary(
    int ToolCount,
    int SelectedCount,
    int InstalledCount,
    int ActiveCount,
    int AttentionCount,
    string Summary,
    string Details);
