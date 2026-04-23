namespace Locora.Domain.Entities;

public sealed record PackageDownloadStatus(
    string PackageId,
    string DisplayName,
    string RequestedVersion,
    string ResolvedVersion,
    string SourceId,
    string State,
    string ChecksumState,
    string? ExpectedSha256,
    string? ActualSha256,
    string ExtractionState,
    string ArtifactSource,
    string CachePath,
    string InstallPath,
    string ActivePath,
    string Summary,
    string Details,
    bool IsCached,
    bool IsExtracted);
