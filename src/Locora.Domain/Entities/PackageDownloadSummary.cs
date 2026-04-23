namespace Locora.Domain.Entities;

public sealed record PackageDownloadSummary(
    int ActiveSelectionCount,
    int CachedCount,
    int PendingCount,
    int MissingCount,
    int ErrorCount,
    int VerifiedChecksumCount,
    int UnverifiedChecksumCount,
    int ChecksumMismatchCount,
    int ExtractedCount,
    int PendingExtractionCount,
    int ExtractionErrorCount,
    string Summary,
    string Details);
