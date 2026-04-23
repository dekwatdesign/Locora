namespace Locora.Domain.Entities;

public sealed record PackageRegistrySummary(
    int EnabledSourceCount,
    int ReadySourceCount,
    int ErrorSourceCount,
    int PackageCount,
    int VersionCount,
    string Summary,
    string Details);
