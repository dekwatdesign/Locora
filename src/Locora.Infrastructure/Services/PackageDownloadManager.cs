using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Infrastructure.Services;

public sealed class PackageDownloadManager
{
    private static readonly Regex VersionTextPattern = new(@"\d+(?:\.\d+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions ReadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly JsonSerializerOptions WriteSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IEnvironmentPaths _paths;
    private readonly IOptionsMonitor<PackagesLockOptions> _lockOptions;
    private readonly PackageSourceRegistry _registry;
    private readonly ILogger<PackageDownloadManager> _logger;

    public PackageDownloadManager(
        IEnvironmentPaths paths,
        IOptionsMonitor<PackagesLockOptions> lockOptions,
        PackageSourceRegistry registry,
        ILogger<PackageDownloadManager> logger)
    {
        _paths = paths;
        _lockOptions = lockOptions;
        _registry = registry;
        _logger = logger;
    }

    public async Task<PackageDownloadManagerSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return (await BuildPlanAsync(cancellationToken)).Snapshot;
    }

    public async Task<RuntimePackageManagerSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return BuildRuntimeSnapshot(await BuildPlanAsync(cancellationToken));
    }

    public async Task<ToolPackageManagerSnapshot> GetToolSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return BuildToolSnapshot(await BuildPlanAsync(cancellationToken));
    }

    public async Task<PackageCompatibilityManagerSnapshot> GetCompatibilitySnapshotAsync(
        IReadOnlyList<PackageCompatibilityRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _registry.GetCatalogAsync(cancellationToken);
        var lockState = await GetLockStateAsync(cancellationToken);
        var checks = requirements
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Key))
            .OrderBy(requirement => requirement.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(requirement => ResolveCompatibility(requirement, catalog, lockState))
            .ToArray();

        var compatibleCount = checks.Count(check => check.IsCompatible);
        var attentionCount = checks.Length - compatibleCount;

        return new PackageCompatibilityManagerSnapshot(
            checks,
            checks.Length,
            compatibleCount,
            attentionCount,
            BuildCompatibilitySummary(checks.Length, compatibleCount, attentionCount),
            BuildCompatibilityDetails(checks, checks.Length, compatibleCount, attentionCount));
    }

    public async Task SyncDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        await RunDownloadSyncAsync(plan.DownloadWorkItems, cancellationToken);
    }

    public async Task ExtractArchivesAsync(CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        await RunArchiveExtractionAsync(plan.LockState, plan.ExtractionWorkItems, cancellationToken);
    }

    public async Task InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        await RunDownloadSyncAsync(plan.DownloadWorkItems, cancellationToken);

        var refreshedPlan = await BuildPlanAsync(cancellationToken);
        await RunArchiveExtractionAsync(refreshedPlan.LockState, refreshedPlan.ExtractionWorkItems, cancellationToken);

        _logger.LogInformation("Package install or update workflow completed for active selections.");
    }

    public async Task RemoveInstalledPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package id is required.", nameof(packageId));
        }

        var plan = await BuildPlanAsync(cancellationToken);
        var installedPackages = plan.LockState.InstalledPackages
            .Where(record => record.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matchingStatuses = plan.Snapshot.Downloads
            .Where(download => download.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (installedPackages.Length == 0 && matchingStatuses.Length == 0)
        {
            _logger.LogInformation("Package removal skipped because {PackageId} was not installed.", packageId);
            return;
        }

        var installPaths = installedPackages
            .Select(record => ResolveLocalPath(record.InstallPath))
            .Concat(matchingStatuses.Select(status => status.InstallPath))
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                !path.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("(not configured)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activePaths = matchingStatuses
            .Select(status => status.ActivePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                !path.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("(not configured)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in installPaths)
        {
            TryDeleteInstallRoot(path);
        }

        foreach (var path in activePaths)
        {
            TryDeleteInstallRoot(path);
        }

        await WriteLockStateAsync(
            new PackagesLockOptions
            {
                SchemaVersion = plan.LockState.SchemaVersion,
                ActiveSelections = [.. plan.LockState.ActiveSelections],
                InstalledPackages =
                [
                    .. plan.LockState.InstalledPackages.Where(record =>
                        !record.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                ]
            },
            cancellationToken);

        _logger.LogInformation(
            "Removed installed package {PackageId}. Deleted {InstallPathCount} install paths and {ActivePathCount} active runtime paths.",
            packageId,
            installPaths.Length,
            activePaths.Length);
    }

    public async Task SelectRuntimeVersionAsync(string selectionKey, CancellationToken cancellationToken = default)
    {
        var (packageId, requestedVersion) = ParseRuntimeSelectionKey(selectionKey);
        var catalog = await _registry.GetCatalogAsync(cancellationToken);
        var lockState = await GetLockStateAsync(cancellationToken);
        var packageSelection = ResolveRuntimePackageVersion(catalog, packageId, requestedVersion);

        var nextSelections = lockState.ActiveSelections
            .Where(selection => !selection.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        nextSelections.Add(
            new PackageSelectionRecord
            {
                PackageId = packageSelection.Package.PackageId,
                RequestedVersion = packageSelection.Version.Version,
                SelectionStrategy = "exact",
                SourceId = packageSelection.Source.Status.Id
            });

        var installedPackages = lockState.InstalledPackages.ToList();
        var installPath = BuildInstallPath(packageSelection.Package, packageSelection.Version);
        var activePath = BuildActivePath(packageSelection.Package);
        var executablePath = BuildChildPath(installPath, packageSelection.Version.ExecutablePath);

        if (File.Exists(executablePath) &&
            packageSelection.Package.SupportsActiveAlias &&
            !activePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase))
        {
            SyncActivePath(installPath, activePath);
            UpsertInstalledPackageRecord(
                installedPackages,
                new PackageExtractionWorkItem(
                    packageSelection.Package.PackageId,
                    packageSelection.Package.DisplayName,
                    packageSelection.Version.Version,
                    packageSelection.Source.Status.Id,
                    string.Empty,
                    packageSelection.Version.ArchiveType,
                    installPath,
                    activePath,
                    packageSelection.Version.ExecutablePath,
                    packageSelection.Version.Sha256,
                    ShouldExtractFromCache: false,
                    ShouldRefreshActivePath: true));
        }

        await WriteLockStateAsync(
            new PackagesLockOptions
            {
                SchemaVersion = lockState.SchemaVersion,
                ActiveSelections = nextSelections
                    .OrderBy(selection => selection.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                InstalledPackages = installedPackages
            },
            cancellationToken);

        _logger.LogInformation(
            "Selected runtime package {PackageId} version {Version} from source {SourceId}",
            packageSelection.Package.PackageId,
            packageSelection.Version.Version,
            packageSelection.Source.Status.Id);
    }

    public async Task ApplyPackageSelectionsAsync(
        IReadOnlyDictionary<string, string> packageSelections,
        CancellationToken cancellationToken = default)
    {
        if (packageSelections.Count == 0)
        {
            return;
        }

        var catalog = await _registry.GetCatalogAsync(cancellationToken);
        var lockState = await GetLockStateAsync(cancellationToken);
        var selectedPackageIds = packageSelections.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSelections = lockState.ActiveSelections
            .Where(selection => !selectedPackageIds.Contains(selection.PackageId))
            .ToList();

        foreach (var selection in packageSelections.OrderBy(selection => selection.Key, StringComparer.OrdinalIgnoreCase))
        {
            var requestedVersion = selection.Value ?? string.Empty;
            var sourceId = ResolvePackageSourceId(catalog, selection.Key) ??
                lockState.ActiveSelections.FirstOrDefault(existing =>
                    existing.PackageId.Equals(selection.Key, StringComparison.OrdinalIgnoreCase))?.SourceId ??
                "locora-bundled";

            nextSelections.Add(
                new PackageSelectionRecord
                {
                    PackageId = selection.Key,
                    RequestedVersion = requestedVersion,
                    SelectionStrategy = requestedVersion.Contains('x') ||
                        requestedVersion.Contains('X') ||
                        requestedVersion.Contains('*')
                            ? "latest-matching"
                            : "exact",
                    SourceId = sourceId
                });
        }

        await WriteLockStateAsync(
            new PackagesLockOptions
            {
                SchemaVersion = lockState.SchemaVersion,
                ActiveSelections = nextSelections
                    .OrderBy(selection => selection.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                InstalledPackages = lockState.InstalledPackages
            },
            cancellationToken);

        _logger.LogInformation(
            "Applied {SelectionCount} package selections from the active stack profile.",
            packageSelections.Count);
    }

    private async Task RunDownloadSyncAsync(
        IReadOnlyList<PackageDownloadWorkItem> workItems,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.PackageCacheRoot);

        var copiedCount = 0;
        var downloadedCount = 0;
        var skippedCount = 0;
        var failureCount = 0;

        foreach (var workItem in workItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(workItem.CachePath))
            {
                skippedCount++;
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(workItem.CachePath) ?? _paths.PackageCacheRoot);

                if (!string.IsNullOrWhiteSpace(workItem.LocalSourcePath))
                {
                    File.Copy(workItem.LocalSourcePath, workItem.CachePath, overwrite: true);
                    copiedCount++;
                }
                else if (workItem.RemoteUri is not null)
                {
                    using var response = await HttpClient.GetAsync(workItem.RemoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var destinationStream = File.Create(workItem.CachePath);
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                    downloadedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (Exception exception)
            {
                failureCount++;
                _logger.LogWarning(
                    exception,
                    "Failed to stage package artifact for {PackageId} {Version} into {CachePath}",
                    workItem.PackageId,
                    workItem.Version,
                    workItem.CachePath);

                TryDeletePartialFile(workItem.CachePath);
            }
        }

        _logger.LogInformation(
            "Package download sync completed: {CopiedCount} copied, {DownloadedCount} downloaded, {FailureCount} failed, {SkippedCount} skipped",
            copiedCount,
            downloadedCount,
            failureCount,
            skippedCount);
    }

    private async Task RunArchiveExtractionAsync(
        PackagesLockOptions lockState,
        IReadOnlyList<PackageExtractionWorkItem> workItems,
        CancellationToken cancellationToken)
    {
        if (workItems.Count == 0)
        {
            _logger.LogInformation("Package archive extraction skipped because no cached packages required extraction.");
            return;
        }

        var installedPackages = lockState.InstalledPackages.ToList();
        var extractedCount = 0;
        var activatedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        foreach (var workItem in workItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (workItem.ShouldExtractFromCache)
                {
                    ExtractArchiveIntoInstallPath(workItem.CachePath, workItem.ArchiveType, workItem.InstallPath, workItem.ExecutablePath);
                    extractedCount++;
                }
                else
                {
                    skippedCount++;
                }

                if (workItem.ShouldRefreshActivePath)
                {
                    SyncActivePath(workItem.InstallPath, workItem.ActivePath);
                    activatedCount++;
                }

                var installedExecutablePath = BuildChildPath(workItem.InstallPath, workItem.ExecutablePath);
                if (!File.Exists(installedExecutablePath))
                {
                    throw new InvalidOperationException($"Expected extracted executable was not found at {installedExecutablePath}.");
                }

                if (!string.IsNullOrWhiteSpace(workItem.ActivePath) &&
                    !workItem.ActivePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase))
                {
                    var activeExecutablePath = BuildChildPath(workItem.ActivePath, workItem.ExecutablePath);
                    if (!File.Exists(activeExecutablePath))
                    {
                        throw new InvalidOperationException($"Expected active runtime executable was not found at {activeExecutablePath}.");
                    }
                }

                UpsertInstalledPackageRecord(installedPackages, workItem);
            }
            catch (Exception exception)
            {
                failedCount++;
                _logger.LogWarning(
                    exception,
                    "Failed to extract cached archive for {PackageId} {Version} into {InstallPath}",
                    workItem.PackageId,
                    workItem.Version,
                    workItem.InstallPath);
            }
        }

        if (failedCount < workItems.Count)
        {
            await WriteLockStateAsync(
                new PackagesLockOptions
                {
                    SchemaVersion = lockState.SchemaVersion,
                    ActiveSelections = [.. lockState.ActiveSelections],
                    InstalledPackages = installedPackages
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "Package archive extraction completed: {ExtractedCount} extracted, {ActivatedCount} active paths refreshed, {FailedCount} failed, {SkippedCount} skipped",
            extractedCount,
            activatedCount,
            failedCount,
            skippedCount);
    }

    private async Task<PackageDownloadPlan> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var catalog = await _registry.GetCatalogAsync(cancellationToken);
        var lockState = await GetLockStateAsync(cancellationToken);
        var selections = lockState.ActiveSelections
            .OrderBy(selection => selection.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(selection => selection.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<PackageDownloadStatusSnapshot>(selections.Length);
        var downloadWorkItems = new List<PackageDownloadWorkItem>();
        var extractionWorkItems = new List<PackageExtractionWorkItem>();

        foreach (var selection in selections)
        {
            var item = ResolveSelection(selection, catalog, lockState.InstalledPackages);
            items.Add(item.Status);

            if (item.DownloadWorkItem is not null)
            {
                downloadWorkItems.Add(item.DownloadWorkItem);
            }

            if (item.ExtractionWorkItem is not null)
            {
                extractionWorkItems.Add(item.ExtractionWorkItem);
            }
        }

        var cachedCount = items.Count(item => item.IsCached);
        var pendingCount = items.Count(item => item.State.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        var missingCount = items.Count(item => item.State.Equals("Missing", StringComparison.OrdinalIgnoreCase));
        var errorCount = items.Count(item => item.State.Equals("Error", StringComparison.OrdinalIgnoreCase));
        var verifiedChecksumCount = items.Count(item => item.ChecksumState.Equals("Verified", StringComparison.OrdinalIgnoreCase));
        var unverifiedChecksumCount = items.Count(item => item.ChecksumState.Equals("Unverified", StringComparison.OrdinalIgnoreCase));
        var checksumMismatchCount = items.Count(item => item.ChecksumState.Equals("Mismatch", StringComparison.OrdinalIgnoreCase));
        var extractedCount = items.Count(item => item.IsExtracted);
        var pendingExtractionCount = items.Count(item => item.ExtractionState.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        var extractionErrorCount = items.Count(item => item.ExtractionState.Equals("Error", StringComparison.OrdinalIgnoreCase));

        return new PackageDownloadPlan(
            new PackageDownloadManagerSnapshot(
                items,
                selections.Length,
                cachedCount,
                pendingCount,
                missingCount,
                errorCount,
                verifiedChecksumCount,
                unverifiedChecksumCount,
                checksumMismatchCount,
                extractedCount,
                pendingExtractionCount,
                extractionErrorCount,
                BuildSummary(selections.Length, cachedCount, pendingCount, missingCount, errorCount, verifiedChecksumCount, unverifiedChecksumCount, checksumMismatchCount, extractedCount, pendingExtractionCount, extractionErrorCount),
                BuildDetails(selections.Length, cachedCount, pendingCount, missingCount, errorCount, verifiedChecksumCount, unverifiedChecksumCount, checksumMismatchCount, extractedCount, pendingExtractionCount, extractionErrorCount)),
            catalog,
            lockState,
            downloadWorkItems,
            extractionWorkItems);
    }

    private RuntimePackageManagerSnapshot BuildRuntimeSnapshot(PackageDownloadPlan plan)
    {
        var runtimePackages = plan.Catalog.Sources
            .Where(source => source.Manifest is not null &&
                source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .SelectMany(source => source.Manifest!.Packages
                .Where(IsRuntimePackage)
                .Select(package => new RuntimeCatalogPackage(source, package)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Package.PackageId))
            .GroupBy(item => item.Package.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.Source.Status.Priority)
                .ThenBy(item => item.Package.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.Package.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var statuses = runtimePackages
            .Select(item => CreateRuntimeStatus(item, plan))
            .ToArray();
        var installedCount = statuses.Count(status => status.IsInstalled);
        var activeCount = statuses.Count(status => status.IsActive);
        var switchableCount = statuses.Count(status => status.SupportsSwitching);
        var attentionCount = statuses.Count(NeedsRuntimeAttention);

        return new RuntimePackageManagerSnapshot(
            statuses,
            statuses.Length,
            installedCount,
            activeCount,
            switchableCount,
            attentionCount,
            BuildRuntimeSummary(statuses.Length, installedCount, activeCount, switchableCount, attentionCount),
            BuildRuntimeDetails(statuses.Length, installedCount, activeCount, switchableCount, attentionCount));
    }

    private ToolPackageManagerSnapshot BuildToolSnapshot(PackageDownloadPlan plan)
    {
        var toolPackages = plan.Catalog.Sources
            .Where(source => source.Manifest is not null &&
                source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .SelectMany(source => source.Manifest!.Packages
                .Where(IsToolPackage)
                .Select(package => new ToolCatalogPackage(source, package)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Package.PackageId))
            .GroupBy(item => item.Package.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.Source.Status.Priority)
                .ThenBy(item => item.Package.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.Package.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var statuses = toolPackages
            .Select(item => CreateToolStatus(item, plan))
            .ToArray();
        var selectedCount = statuses.Count(status => status.IsSelected);
        var installedCount = statuses.Count(status => status.IsInstalled);
        var activeCount = statuses.Count(status => status.IsActive);
        var attentionCount = statuses.Count(NeedsToolAttention);

        return new ToolPackageManagerSnapshot(
            statuses,
            statuses.Length,
            selectedCount,
            installedCount,
            activeCount,
            attentionCount,
            BuildToolSummary(statuses.Length, selectedCount, installedCount, activeCount, attentionCount),
            BuildToolDetails(statuses.Length, selectedCount, installedCount, activeCount, attentionCount));
    }

    private RuntimePackageStatusSnapshot CreateRuntimeStatus(RuntimeCatalogPackage catalogPackage, PackageDownloadPlan plan)
    {
        var package = catalogPackage.Package;
        var selection = plan.LockState.ActiveSelections.FirstOrDefault(candidate =>
            candidate.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
        var requestedVersion = string.IsNullOrWhiteSpace(selection?.RequestedVersion)
            ? package.DefaultVersion
            : selection.RequestedVersion;
        var resolvedVersion = SelectVersion(package, requestedVersion);
        var download = plan.Snapshot.Downloads.FirstOrDefault(candidate =>
            candidate.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
        var availableVersions = SortVersionLabels(package.Versions.Select(version => version.Version));
        var installedVersions = ResolveInstalledVersions(package, plan.LockState.InstalledPackages);
        var activeVersion = ResolveActiveVersion(package, plan.LockState.InstalledPackages, resolvedVersion?.Version, download);
        var installRoot = ResolveLocalPath(package.InstallRootPath);
        var activePath = BuildActivePath(package);
        var executablePath = resolvedVersion is null
            ? package.Versions.FirstOrDefault()?.ExecutablePath ?? "(unresolved)"
            : BuildChildPath(BuildInstallPath(package, resolvedVersion), resolvedVersion.ExecutablePath);
        var isInstalled = resolvedVersion is not null &&
            installedVersions.Any(version => version.Equals(resolvedVersion.Version, StringComparison.OrdinalIgnoreCase));
        var isActive = download?.IsExtracted == true;
        var state = ResolveRuntimeState(download, resolvedVersion, isInstalled);
        var supportsSwitching = package.SupportsSideBySideInstall &&
            package.SupportsActiveAlias &&
            availableVersions.Count > 1;
        var sourceId = string.IsNullOrWhiteSpace(selection?.SourceId)
            ? catalogPackage.Source.Status.Id
            : selection.SourceId;
        var channel = resolvedVersion?.Channel ?? catalogPackage.Source.Status.Channel;

        return new RuntimePackageStatusSnapshot(
            package.PackageId,
            string.IsNullOrWhiteSpace(package.DisplayName) ? package.PackageId : package.DisplayName,
            package.Family,
            package.Kind,
            state,
            string.IsNullOrWhiteSpace(requestedVersion) ? "(default)" : requestedVersion,
            resolvedVersion?.Version ?? "Unavailable",
            activeVersion,
            package.DefaultVersion,
            sourceId,
            channel,
            installRoot,
            activePath,
            executablePath,
            BuildRuntimePackageSummary(package, download, resolvedVersion, isInstalled, isActive),
            BuildRuntimePackageDetails(package, download, resolvedVersion, installedVersions, activePath),
            availableVersions,
            installedVersions,
            supportsSwitching,
            isInstalled,
            isActive);
    }

    private ToolPackageStatusSnapshot CreateToolStatus(ToolCatalogPackage catalogPackage, PackageDownloadPlan plan)
    {
        var package = catalogPackage.Package;
        var selection = plan.LockState.ActiveSelections.FirstOrDefault(candidate =>
            candidate.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
        var hasSelection = selection is not null;
        var requestedVersion = string.IsNullOrWhiteSpace(selection?.RequestedVersion)
            ? hasSelection
                ? package.DefaultVersion
                : "(not selected)"
            : selection!.RequestedVersion;
        var resolvedVersion = hasSelection
            ? SelectVersion(package, requestedVersion)
            : SelectVersion(package, package.DefaultVersion) ?? package.Versions.FirstOrDefault();
        var download = plan.Snapshot.Downloads.FirstOrDefault(candidate =>
            candidate.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
        var availableVersions = SortVersionLabels(package.Versions.Select(version => version.Version));
        var installedVersions = ResolveInstalledVersions(package, plan.LockState.InstalledPackages);
        var activeVersion = ResolveActiveVersion(package, plan.LockState.InstalledPackages, resolvedVersion?.Version, download);
        var installRoot = ResolveLocalPath(package.InstallRootPath);
        var activePath = BuildActivePath(package);
        var executablePath = resolvedVersion is null
            ? package.Versions.FirstOrDefault()?.ExecutablePath ?? "(unresolved)"
            : BuildChildPath(BuildInstallPath(package, resolvedVersion), resolvedVersion.ExecutablePath);
        var isInstalled = resolvedVersion is not null &&
            installedVersions.Any(version => version.Equals(resolvedVersion.Version, StringComparison.OrdinalIgnoreCase));
        var isActive = !string.IsNullOrWhiteSpace(activeVersion) &&
            !activeVersion.Equals("(none)", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(activePath) &&
            !activePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(activePath);
        var state = ResolveToolState(download, resolvedVersion, isInstalled, hasSelection, isActive);
        var sourceId = string.IsNullOrWhiteSpace(selection?.SourceId)
            ? catalogPackage.Source.Status.Id
            : selection.SourceId;
        var channel = resolvedVersion?.Channel ?? catalogPackage.Source.Status.Channel;
        var providedCommands = resolvedVersion?.ProvidesCommands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return new ToolPackageStatusSnapshot(
            package.PackageId,
            string.IsNullOrWhiteSpace(package.DisplayName) ? package.PackageId : package.DisplayName,
            package.Family,
            package.Kind,
            state,
            requestedVersion,
            resolvedVersion?.Version ?? "Unavailable",
            activeVersion,
            package.DefaultVersion,
            sourceId,
            channel,
            installRoot,
            activePath,
            executablePath,
            BuildToolPackageSummary(package, download, hasSelection, resolvedVersion, isInstalled, isActive),
            BuildToolPackageDetails(package, download, hasSelection, resolvedVersion, installedVersions, activePath, providedCommands),
            availableVersions,
            installedVersions,
            providedCommands,
            hasSelection,
            isInstalled,
            isActive);
    }

    private RuntimePackageSelection ResolveRuntimePackageVersion(
        PackageCatalogSnapshot catalog,
        string packageId,
        string requestedVersion)
    {
        var sources = catalog.Sources
            .Where(source => source.Manifest is not null &&
                source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.Status.Priority)
            .ThenBy(source => source.Status.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in sources)
        {
            var package = source.Manifest!.Packages.FirstOrDefault(candidate =>
                candidate.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
            if (package is null || !IsRuntimePackage(package))
            {
                continue;
            }

            var version = SelectVersion(package, requestedVersion);
            if (version is null)
            {
                var availableVersions = string.Join(", ", SortVersionLabels(package.Versions.Select(candidate => candidate.Version)).Take(6));
                throw new InvalidOperationException(
                    $"Runtime package '{packageId}' does not provide version '{requestedVersion}'. Available versions: {availableVersions}.");
            }

            return new RuntimePackageSelection(source, package, version);
        }

        throw new InvalidOperationException($"Runtime package '{packageId}' was not found in any ready package source.");
    }

    private PackageCompatibilityStatusSnapshot ResolveCompatibility(
        PackageCompatibilityRequirement requirement,
        PackageCatalogSnapshot catalog,
        PackagesLockOptions lockState)
    {
        if (string.IsNullOrWhiteSpace(requirement.PackageId))
        {
            return PackageCompatibilityStatusSnapshot.Attention(
                requirement,
                "(unknown)",
                "Unavailable",
                "Service package id could not be inferred.",
                $"Set {requirement.DisplayName} RelativeExecutablePath to a portable bin path such as bin/<package>/current/<executable>.");
        }

        var readySources = catalog.Sources
            .Where(source => source.Manifest is not null &&
                source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.Status.Priority)
            .ThenBy(source => source.Status.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selection = lockState.ActiveSelections.FirstOrDefault(candidate =>
            candidate.PackageId.Equals(requirement.PackageId, StringComparison.OrdinalIgnoreCase));
        var selectedVersion = selection?.RequestedVersion ?? "(none)";

        if (selection is null)
        {
            return PackageCompatibilityStatusSnapshot.Attention(
                requirement,
                selectedVersion,
                "Unavailable",
                $"{requirement.DisplayName} has no active package selection.",
                $"Add {requirement.PackageId} to LocoraPackagesLock.ActiveSelections so package install/update can satisfy this service requirement.");
        }

        var sources = ResolveSourceCandidates(selection, catalog, readySources);
        if (sources.Count == 0)
        {
            return PackageCompatibilityStatusSnapshot.Attention(
                requirement,
                selectedVersion,
                "Unavailable",
                $"{requirement.DisplayName} points at a package source that is not ready.",
                string.IsNullOrWhiteSpace(selection.SourceId)
                    ? $"No ready package source contains {requirement.PackageId}."
                    : $"Repair or enable package source '{selection.SourceId}', then refresh diagnostics.");
        }

        foreach (var source in sources)
        {
            var package = source.Manifest?.Packages.FirstOrDefault(candidate =>
                candidate.PackageId.Equals(requirement.PackageId, StringComparison.OrdinalIgnoreCase));
            if (package is null)
            {
                continue;
            }

            var resolvedVersion = SelectVersion(package, selection.RequestedVersion);
            if (resolvedVersion is null)
            {
                var availableVersions = string.Join(", ", SortVersionLabels(package.Versions.Select(version => version.Version)).Take(6));
                return PackageCompatibilityStatusSnapshot.Attention(
                    requirement,
                    selectedVersion,
                    "Unavailable",
                    $"{requirement.DisplayName} package selection does not resolve.",
                    $"Selected {requirement.PackageId} {selectedVersion}, but source {source.Status.DisplayName} only provides: {availableVersions}.");
            }

            if (!MatchesRequestedVersion(ExtractVersionParts(resolvedVersion.Version), ExtractVersionParts(requirement.RequestedVersion)))
            {
                return PackageCompatibilityStatusSnapshot.Attention(
                    requirement,
                    selectedVersion,
                    resolvedVersion.Version,
                    $"{requirement.DisplayName} expects {requirement.RequestedVersion}, but packages.lock selects {resolvedVersion.Version}.",
                    $"Change the {requirement.PackageId} package selection to a version matching {requirement.RequestedVersion}, or update the service Version in services.json.");
            }

            var expectedExecutablePath = NormalizePortablePath(requirement.RelativeExecutablePath);
            var compatibleExecutablePaths = BuildCompatibleExecutablePaths(package, resolvedVersion);
            if (!compatibleExecutablePaths.Contains(expectedExecutablePath, StringComparer.OrdinalIgnoreCase))
            {
                return PackageCompatibilityStatusSnapshot.Attention(
                    requirement,
                    selectedVersion,
                    resolvedVersion.Version,
                    $"{requirement.DisplayName} executable path does not match the selected package layout.",
                    $"Configured executable: {requirement.RelativeExecutablePath}. Expected one of: {string.Join(", ", compatibleExecutablePaths)}.");
            }

            return new PackageCompatibilityStatusSnapshot(
                requirement.Key,
                requirement.DisplayName,
                requirement.PackageId,
                requirement.RequestedVersion,
                selectedVersion,
                resolvedVersion.Version,
                "Ready",
                $"{requirement.DisplayName} matches {requirement.PackageId} {resolvedVersion.Version}.",
                $"Service version {requirement.RequestedVersion}, selected version {selectedVersion}, source {source.Status.Id}, executable {requirement.RelativeExecutablePath}.",
                true);
        }

        return PackageCompatibilityStatusSnapshot.Attention(
            requirement,
            selectedVersion,
            "Unavailable",
            $"{requirement.DisplayName} package was not found in the selected source.",
            $"Add package {requirement.PackageId} to source '{selection.SourceId}' or point the lock entry at a source that contains it.");
    }

    private static (string PackageId, string RequestedVersion) ParseRuntimeSelectionKey(string selectionKey)
    {
        var parts = (selectionKey ?? string.Empty).Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException("Runtime selection must include a package id and version.", nameof(selectionKey));
        }

        return (parts[0], parts[1]);
    }

    private IReadOnlyList<string> ResolveInstalledVersions(
        PackageManifestPackage package,
        IReadOnlyList<InstalledPackageRecord> installedPackages)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in installedPackages.Where(record =>
            record.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(record.Version))
            {
                versions.Add(record.Version);
            }
        }

        foreach (var version in package.Versions)
        {
            var executablePath = BuildChildPath(BuildInstallPath(package, version), version.ExecutablePath);
            if (File.Exists(executablePath))
            {
                versions.Add(version.Version);
            }
        }

        return SortVersionLabels(versions);
    }

    private static string ResolveActiveVersion(
        PackageManifestPackage package,
        IReadOnlyList<InstalledPackageRecord> installedPackages,
        string? resolvedVersion,
        PackageDownloadStatusSnapshot? download)
    {
        if (download?.IsExtracted == true && !string.IsNullOrWhiteSpace(resolvedVersion))
        {
            return resolvedVersion;
        }

        var activeRecord = installedPackages
            .Where(record =>
                record.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase) &&
                record.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.InstalledAtUtc)
            .FirstOrDefault();

        return activeRecord?.Version ?? "(none)";
    }

    private static string ResolveRuntimeState(
        PackageDownloadStatusSnapshot? download,
        PackageManifestVersion? resolvedVersion,
        bool isInstalled)
    {
        if (resolvedVersion is null)
        {
            return "Error";
        }

        if (download is null)
        {
            return isInstalled ? "Installed" : "Cataloged";
        }

        if (download.State.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            download.ExtractionState.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (download.IsExtracted)
        {
            return "Active";
        }

        if (download.State.Equals("Missing", StringComparison.OrdinalIgnoreCase))
        {
            return "MissingArtifact";
        }

        if (download.State.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return "PendingDownload";
        }

        if (download.State.Equals("Cached", StringComparison.OrdinalIgnoreCase) &&
            download.ExtractionState.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return "ReadyToExtract";
        }

        return isInstalled ? "Installed" : download.State;
    }

    private static string ResolveToolState(
        PackageDownloadStatusSnapshot? download,
        PackageManifestVersion? resolvedVersion,
        bool isInstalled,
        bool isSelected,
        bool isActive)
    {
        if (!isSelected)
        {
            return isActive ? "Active" : isInstalled ? "Installed" : "Cataloged";
        }

        return isActive
            ? "Active"
            : ResolveRuntimeState(download, resolvedVersion, isInstalled);
    }

    private static IReadOnlyList<string> BuildCompatibleExecutablePaths(
        PackageManifestPackage package,
        PackageManifestVersion version)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installRoot = NormalizePortablePath(package.InstallRootPath);
        var relativeInstallPath = NormalizePortablePath(version.RelativeInstallPath);
        var executablePath = NormalizePortablePath(version.ExecutablePath);

        if (!string.IsNullOrWhiteSpace(package.ActiveAliasPath) && package.SupportsActiveAlias)
        {
            paths.Add(CombinePortablePath(package.ActiveAliasPath, executablePath));
        }

        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            paths.Add(CombinePortablePath(installRoot, relativeInstallPath, executablePath));
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CombinePortablePath(params string?[] segments)
    {
        return string.Join(
            '/',
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment) &&
                    !segment.Equals(".", StringComparison.OrdinalIgnoreCase))
                .Select(segment => NormalizePortablePath(segment!)));
    }

    private static string NormalizePortablePath(string path)
    {
        return (path ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .Trim('/');
    }

    private static bool NeedsRuntimeAttention(RuntimePackageStatusSnapshot status)
    {
        return status.State.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            status.State.Equals("MissingArtifact", StringComparison.OrdinalIgnoreCase) ||
            status.State.Equals("PendingDownload", StringComparison.OrdinalIgnoreCase) ||
            status.State.Equals("ReadyToExtract", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsToolAttention(ToolPackageStatusSnapshot status)
    {
        return status.IsSelected &&
            (status.State.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
             status.State.Equals("MissingArtifact", StringComparison.OrdinalIgnoreCase) ||
             status.State.Equals("PendingDownload", StringComparison.OrdinalIgnoreCase) ||
             status.State.Equals("ReadyToExtract", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildRuntimePackageSummary(
        PackageManifestPackage package,
        PackageDownloadStatusSnapshot? download,
        PackageManifestVersion? resolvedVersion,
        bool isInstalled,
        bool isActive)
    {
        if (download is not null)
        {
            return download.Summary;
        }

        if (resolvedVersion is null)
        {
            return $"No version from {package.DisplayName} matched the selected request.";
        }

        if (isActive)
        {
            return $"{package.DisplayName} {resolvedVersion.Version} is active.";
        }

        return isInstalled
            ? $"{package.DisplayName} {resolvedVersion.Version} is installed but not active."
            : $"{package.DisplayName} {resolvedVersion.Version} is available in the catalog.";
    }

    private static string BuildRuntimePackageDetails(
        PackageManifestPackage package,
        PackageDownloadStatusSnapshot? download,
        PackageManifestVersion? resolvedVersion,
        IReadOnlyList<string> installedVersions,
        string activePath)
    {
        if (download is not null)
        {
            return download.Details;
        }

        var availableVersions = package.Versions.Count == 0
            ? "none"
            : string.Join(", ", SortVersionLabels(package.Versions.Select(version => version.Version)));
        var installedLabel = installedVersions.Count == 0
            ? "none"
            : string.Join(", ", installedVersions);

        return resolvedVersion is null
            ? $"Available versions: {availableVersions}. Installed versions: {installedLabel}."
            : $"Selected version: {resolvedVersion.Version}. Installed versions: {installedLabel}. Active alias path: {activePath}.";
    }

    private static string BuildToolPackageSummary(
        PackageManifestPackage package,
        PackageDownloadStatusSnapshot? download,
        bool isSelected,
        PackageManifestVersion? resolvedVersion,
        bool isInstalled,
        bool isActive)
    {
        if (!isSelected)
        {
            return $"{package.DisplayName} is cataloged but not selected in packages.lock.json.";
        }

        if (download is not null)
        {
            return download.Summary;
        }

        if (resolvedVersion is null)
        {
            return $"No version from {package.DisplayName} matched the selected request.";
        }

        if (isActive)
        {
            return $"{package.DisplayName} {resolvedVersion.Version} is active.";
        }

        return isInstalled
            ? $"{package.DisplayName} {resolvedVersion.Version} is installed but not active."
            : $"{package.DisplayName} {resolvedVersion.Version} is selected and ready to stage.";
    }

    private static string BuildToolPackageDetails(
        PackageManifestPackage package,
        PackageDownloadStatusSnapshot? download,
        bool isSelected,
        PackageManifestVersion? resolvedVersion,
        IReadOnlyList<string> installedVersions,
        string activePath,
        IReadOnlyList<string> providedCommands)
    {
        var commandsLabel = providedCommands.Count == 0
            ? "none"
            : string.Join(", ", providedCommands);
        var installedLabel = installedVersions.Count == 0
            ? "none"
            : string.Join(", ", installedVersions);
        var availableVersions = package.Versions.Count == 0
            ? "none"
            : string.Join(", ", SortVersionLabels(package.Versions.Select(version => version.Version)));

        if (!isSelected)
        {
            return $"Add {package.PackageId} to LocoraPackagesLock.ActiveSelections to stage it into {activePath}. Commands: {commandsLabel}. Available versions: {availableVersions}.";
        }

        if (download is not null)
        {
            return $"{download.Details} Commands: {commandsLabel}.";
        }

        return resolvedVersion is null
            ? $"Available versions: {availableVersions}. Installed versions: {installedLabel}. Commands: {commandsLabel}."
            : $"Selected version: {resolvedVersion.Version}. Installed versions: {installedLabel}. Active alias path: {activePath}. Commands: {commandsLabel}.";
    }

    private string BuildRuntimeSummary(
        int runtimeCount,
        int installedCount,
        int activeCount,
        int switchableCount,
        int attentionCount)
    {
        if (runtimeCount == 0)
        {
            return "No runtime packages are cataloged yet.";
        }

        if (attentionCount > 0)
        {
            return $"{attentionCount}/{runtimeCount} runtime selections need package cache or extraction attention.";
        }

        if (activeCount > 0)
        {
            return $"{activeCount}/{runtimeCount} runtime packages have active versions, {switchableCount} support switching.";
        }

        if (installedCount > 0)
        {
            return $"{installedCount}/{runtimeCount} runtime packages are installed, but no active version alias is ready yet.";
        }

        return $"{runtimeCount} runtime packages are cataloged and ready for selection.";
    }

    private string BuildRuntimeDetails(
        int runtimeCount,
        int installedCount,
        int activeCount,
        int switchableCount,
        int attentionCount)
    {
        if (runtimeCount == 0)
        {
            return "Add runtime package entries such as PHP, Node.js, Python, or Java to a ready package manifest.";
        }

        if (attentionCount > 0)
        {
            return $"Use Install or Update Active Packages to stage missing archives, extract cached runtime packages, and refresh active aliases under {_paths.BinRoot}.";
        }

        if (switchableCount > 0)
        {
            return "Use the runtime version controls to update packages.lock.json. Installed versions switch immediately; missing versions become the next active package install target.";
        }

        return $"Runtime packages are cataloged from package manifests. Installed: {installedCount}. Active: {activeCount}.";
    }

    private string BuildToolSummary(
        int toolCount,
        int selectedCount,
        int installedCount,
        int activeCount,
        int attentionCount)
    {
        if (toolCount == 0)
        {
            return "No tool packages are cataloged yet.";
        }

        if (attentionCount > 0)
        {
            return $"{attentionCount}/{selectedCount} selected tool packages need package cache or extraction attention.";
        }

        if (activeCount > 0)
        {
            return $"{activeCount}/{toolCount} tool packages are active, {installedCount} installed, {selectedCount} selected.";
        }

        if (installedCount > 0)
        {
            return $"{installedCount}/{toolCount} tool packages are installed and ready for terminal use.";
        }

        if (selectedCount > 0)
        {
            return $"{selectedCount}/{toolCount} tool packages are selected and ready to stage from the package cache.";
        }

        return $"{toolCount} tool packages are cataloged and available to select.";
    }

    private string BuildToolDetails(
        int toolCount,
        int selectedCount,
        int installedCount,
        int activeCount,
        int attentionCount)
    {
        if (toolCount == 0)
        {
            return "Add tool package entries such as Composer or mkcert to a ready package manifest.";
        }

        if (attentionCount > 0)
        {
            return $"Use Install or Update Active Packages to stage missing archives, extract cached tool packages, and refresh active aliases under {_paths.BinRoot}.";
        }

        if (selectedCount == 0)
        {
            return "Tool packages are cataloged from package manifests. Add entries to packages.lock.json when you want Locora to stage them into the portable bin roots.";
        }

        if (activeCount > 0 || installedCount > 0)
        {
            return "Installed tool packages are prepended to project terminal PATH entries when their active or installed binaries are available.";
        }

        return "Selected tool packages follow the same sync, checksum, and extraction lifecycle as runtime and service packages.";
    }

    private static string BuildCompatibilitySummary(int requirementCount, int compatibleCount, int attentionCount)
    {
        if (requirementCount == 0)
        {
            return "No package-backed service requirements were detected.";
        }

        return attentionCount == 0
            ? $"{compatibleCount}/{requirementCount} package-backed service requirements are compatible."
            : $"{attentionCount}/{requirementCount} package-backed service requirements need attention.";
    }

    private static string BuildCompatibilityDetails(
        IReadOnlyList<PackageCompatibilityStatusSnapshot> checks,
        int requirementCount,
        int compatibleCount,
        int attentionCount)
    {
        if (requirementCount == 0)
        {
            return "Services did not declare portable bin paths that can be matched to package selections.";
        }

        if (attentionCount == 0)
        {
            return $"All package-backed services match the active package lock selections. Compatible requirements: {compatibleCount}.";
        }

        var failingChecks = checks
            .Where(check => !check.IsCompatible)
            .Take(4)
            .Select(check => $"{check.DisplayName}: {check.Summary}");

        return $"Resolve package/service mismatches before relying on profiles or service starts. {string.Join(" ", failingChecks)}";
    }

    private PackageDownloadPlanItem ResolveSelection(
        PackageSelectionRecord selection,
        PackageCatalogSnapshot catalog,
        IReadOnlyList<InstalledPackageRecord> installedPackages)
    {
        var selectionId = selection.PackageId;
        var readySources = catalog.Sources
            .Where(source => source.Manifest is not null && source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.Status.Priority)
            .ThenBy(source => source.Status.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (readySources.Length == 0)
        {
            return PackageDownloadPlanItem.Error(
                selectionId,
                selection.PackageId,
                selection.RequestedVersion,
                selection.SourceId,
                "Package catalog is not ready.",
                "Resolve package source registry errors before syncing package downloads.");
        }

        var sourceCandidates = ResolveSourceCandidates(selection, catalog, readySources);
        if (sourceCandidates.Count == 0)
        {
            return PackageDownloadPlanItem.Error(
                selectionId,
                selection.PackageId,
                selection.RequestedVersion,
                selection.SourceId,
                "Requested package source was not found.",
                string.IsNullOrWhiteSpace(selection.SourceId)
                    ? "No ready package source contained this package selection."
                    : $"Enable or repair source '{selection.SourceId}', then refresh.");
        }

        foreach (var source in sourceCandidates)
        {
            if (source.Manifest is null)
            {
                return PackageDownloadPlanItem.Error(
                    selectionId,
                    selection.PackageId,
                    selection.RequestedVersion,
                    source.Status.Id,
                    source.Status.Summary,
                    source.Status.Details);
            }

            var package = source.Manifest!.Packages
                .FirstOrDefault(candidate => candidate.PackageId.Equals(selection.PackageId, StringComparison.OrdinalIgnoreCase));

            if (package is null)
            {
                continue;
            }

            var requestedVersion = string.IsNullOrWhiteSpace(selection.RequestedVersion)
                ? package.DefaultVersion
                : selection.RequestedVersion;
            var resolvedVersion = SelectVersion(package, requestedVersion);

            if (resolvedVersion is null)
            {
                var availableVersions = package.Versions
                    .Select(version => version.Version)
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .DefaultIfEmpty("(none)")
                    .Take(5);

                return PackageDownloadPlanItem.Error(
                    selectionId,
                    package.DisplayName,
                    requestedVersion,
                    source.Status.Id,
                    $"No package version matched {requestedVersion}.",
                    $"Available versions from {source.Status.DisplayName}: {string.Join(", ", availableVersions)}.");
            }

            var installedRecord = installedPackages.FirstOrDefault(record =>
                record.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase) &&
                record.Version.Equals(resolvedVersion.Version, StringComparison.OrdinalIgnoreCase));

            return CreatePlannedItem(selectionId, source, package, resolvedVersion, requestedVersion, installedRecord);
        }

        return PackageDownloadPlanItem.Error(
            selectionId,
            selection.PackageId,
            selection.RequestedVersion,
            selection.SourceId,
            "Package was not found in any ready source.",
            $"Add {selection.PackageId} to the selected manifest or point the lock entry at a source that contains it.");
    }

    private PackageDownloadPlanItem CreatePlannedItem(
        string selectionId,
        PackageCatalogSource source,
        PackageManifestPackage package,
        PackageManifestVersion version,
        string requestedVersion,
        InstalledPackageRecord? installedRecord)
    {
        var artifactSource = ResolveArtifactSource(version);
        var artifactLabel = artifactSource.DisplayValue;
        var cachePath = BuildCachePath(package.PackageId, version.Version, artifactSource.FileName, version.ArchiveType);
        var expectedSha256 = NormalizeSha256(version.Sha256);
        var installPath = BuildInstallPath(package, version);
        var activePath = BuildActivePath(package);

        if (File.Exists(cachePath))
        {
            var checksumValidation = ValidateCachedArtifact(cachePath, expectedSha256);
            var extractionPlan = PlanExtraction(package, version, source.Status.Id, cachePath, installPath, activePath, checksumValidation.ActualSha256 ?? checksumValidation.ExpectedSha256, checksumValidation.IsReady);
            var summary = checksumValidation.IsReady
                ? extractionPlan.IsExtracted
                    ? "Artifact cached and extracted"
                    : extractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                        ? extractionPlan.Summary
                        : "Artifact cached and ready to extract"
                : checksumValidation.Summary;
            var details = checksumValidation.IsReady
                ? extractionPlan.Details
                : checksumValidation.Details;

            return new PackageDownloadPlanItem(
                new PackageDownloadStatusSnapshot(
                    selectionId,
                    package.DisplayName,
                    requestedVersion,
                    version.Version,
                    source.Status.Id,
                    checksumValidation.IsReady ? "Cached" : "Error",
                    checksumValidation.State,
                    checksumValidation.ExpectedSha256,
                    checksumValidation.ActualSha256,
                    extractionPlan.State,
                    artifactLabel,
                    cachePath,
                    installPath,
                    activePath,
                    summary,
                    details,
                    checksumValidation.IsReady,
                    extractionPlan.IsExtracted),
                null,
                extractionPlan.WorkItem);
        }

        if (!string.IsNullOrWhiteSpace(artifactSource.LocalPath) && File.Exists(artifactSource.LocalPath))
        {
            var extractionPlan = PlanExtraction(package, version, source.Status.Id, cachePath, installPath, activePath, installedRecord?.Sha256, cacheReady: false);
            var summary = extractionPlan.IsExtracted
                ? "Installed version already available"
                : extractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? extractionPlan.Summary
                    : "Ready to stage local artifact";
            var details = extractionPlan.IsExtracted
                ? CombineDetails(extractionPlan.Details, $"Locora can still copy the source artifact into the package cache on the next sync. Source: {artifactSource.LocalPath}")
                : extractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? extractionPlan.Details
                    : $"Locora can copy the source artifact into the package cache on the next sync. Source: {artifactSource.LocalPath}";

            return new PackageDownloadPlanItem(
                new PackageDownloadStatusSnapshot(
                    selectionId,
                    package.DisplayName,
                    requestedVersion,
                    version.Version,
                    source.Status.Id,
                    "Pending",
                    "Pending",
                    expectedSha256,
                    null,
                    extractionPlan.State,
                    artifactLabel,
                    cachePath,
                    installPath,
                    activePath,
                    summary,
                    details,
                    false,
                    extractionPlan.IsExtracted),
                new PackageDownloadWorkItem(package.PackageId, version.Version, cachePath, artifactSource.LocalPath, null),
                extractionPlan.WorkItem);
        }

        if (artifactSource.RemoteUri is not null)
        {
            var extractionPlan = PlanExtraction(package, version, source.Status.Id, cachePath, installPath, activePath, installedRecord?.Sha256, cacheReady: false);
            var summary = extractionPlan.IsExtracted
                ? "Installed version already available"
                : extractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? extractionPlan.Summary
                    : "Ready to download";
            var details = extractionPlan.IsExtracted
                ? CombineDetails(extractionPlan.Details, $"Locora can download this artifact into the package cache on the next sync. URL: {artifactSource.RemoteUri}")
                : extractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? extractionPlan.Details
                    : $"Locora can download this artifact into the package cache on the next sync. URL: {artifactSource.RemoteUri}";

            return new PackageDownloadPlanItem(
                new PackageDownloadStatusSnapshot(
                    selectionId,
                    package.DisplayName,
                    requestedVersion,
                    version.Version,
                    source.Status.Id,
                    "Pending",
                    "Pending",
                    expectedSha256,
                    null,
                    extractionPlan.State,
                    artifactLabel,
                    cachePath,
                    installPath,
                    activePath,
                    summary,
                    details,
                    false,
                    extractionPlan.IsExtracted),
                new PackageDownloadWorkItem(package.PackageId, version.Version, cachePath, null, artifactSource.RemoteUri),
                extractionPlan.WorkItem);
        }

        var missingExtractionPlan = PlanExtraction(package, version, source.Status.Id, cachePath, installPath, activePath, installedRecord?.Sha256, cacheReady: false);
        var missingSummary = missingExtractionPlan.IsExtracted
            ? "Installed version already available"
            : missingExtractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? missingExtractionPlan.Summary
                : "Artifact source is missing";
        var missingDetails = missingExtractionPlan.IsExtracted
            ? CombineDetails(
                missingExtractionPlan.Details,
                string.IsNullOrWhiteSpace(version.DownloadUri)
                    ? "Locora could not resolve a source archive to refresh the package cache, but the extracted runtime is still present."
                    : $"Locora could not resolve the configured artifact source, but the extracted runtime is still present. Expected source: {artifactLabel}")
            : missingExtractionPlan.State.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? missingExtractionPlan.Details
                : string.IsNullOrWhiteSpace(version.DownloadUri)
                    ? "Add the archive at the expected artifact path or set DownloadUri in the manifest so Locora can populate the package cache."
                    : $"The configured artifact source could not be resolved. Expected: {artifactLabel}";

        return new PackageDownloadPlanItem(
            new PackageDownloadStatusSnapshot(
                selectionId,
                package.DisplayName,
                requestedVersion,
                version.Version,
                source.Status.Id,
                "Missing",
                "Pending",
                expectedSha256,
                null,
                missingExtractionPlan.State,
                artifactLabel,
                cachePath,
                installPath,
                activePath,
                missingSummary,
                missingDetails,
                false,
                missingExtractionPlan.IsExtracted),
            null,
            missingExtractionPlan.WorkItem);
    }

    private PackageExtractionPlan PlanExtraction(
        PackageManifestPackage package,
        PackageManifestVersion version,
        string sourceId,
        string cachePath,
        string installPath,
        string activePath,
        string? sha256,
        bool cacheReady)
    {
        if (installPath.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageExtractionPlan(
                "Error",
                "Install root path is not configured",
                $"Set InstallRootPath and RelativeInstallPath for {package.DisplayName} in the package manifest before extracting archives.",
                false,
                null);
        }

        var installExecutablePath = BuildChildPath(installPath, version.ExecutablePath);
        var activeAliasConfigured = package.SupportsActiveAlias && !string.IsNullOrWhiteSpace(package.ActiveAliasPath);
        if (activeAliasConfigured && activePath.Equals("(unset)", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageExtractionPlan(
                "Error",
                "Active runtime path could not be resolved",
                $"Set ActiveAliasPath for {package.DisplayName} in the package manifest before extracting archives.",
                false,
                null);
        }

        var activeExecutablePath = activeAliasConfigured ? BuildChildPath(activePath, version.ExecutablePath) : string.Empty;
        var installReady = File.Exists(installExecutablePath);
        var activeReady = !activeAliasConfigured || File.Exists(activeExecutablePath);
        var archiveType = NormalizeArchiveType(version.ArchiveType);
        var brokenInstall = !installReady && Directory.Exists(installPath);

        if (installReady && activeReady)
        {
            return new PackageExtractionPlan(
                "Extracted",
                $"Archive extracted into {installPath}",
                activeAliasConfigured
                    ? $"Install path: {installPath}. Active runtime path: {activePath}."
                    : $"Install path: {installPath}.",
                true,
                null);
        }

        if (installReady && activeAliasConfigured && !activeReady)
        {
            return new PackageExtractionPlan(
                "Pending",
                "Installed version exists but the active runtime path needs refresh",
                $"Locora can rebuild {activePath} from {installPath} so services that point at the current runtime folder can start again.",
                false,
                new PackageExtractionWorkItem(
                    package.PackageId,
                    package.DisplayName,
                    version.Version,
                    sourceId,
                    cachePath,
                    version.ArchiveType,
                    installPath,
                    activePath,
                    version.ExecutablePath,
                    sha256,
                    ShouldExtractFromCache: false,
                    ShouldRefreshActivePath: true));
        }

        if (!archiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageExtractionPlan(
                "Error",
                $"Archive type '{version.ArchiveType}' is not supported for extraction yet",
                $"Cached artifacts for {package.DisplayName} can only be extracted automatically when ArchiveType is zip. Update the manifest or install this runtime manually into {installPath}.",
                false,
                null);
        }

        if (brokenInstall && !cacheReady)
        {
            return new PackageExtractionPlan(
                "Error",
                "Install path is missing its expected executable",
                $"Expected executable: {installExecutablePath}. Re-stage the archive into {cachePath}, then extract again to repair {installPath}.",
                false,
                null);
        }

        if (cacheReady)
        {
            return new PackageExtractionPlan(
                "Pending",
                brokenInstall
                    ? "Install path needs repair from the cached archive"
                    : "Cached archive is ready to extract",
                activeAliasConfigured
                    ? $"Locora can extract this archive into {installPath} and refresh {activePath} as the active runtime path."
                    : $"Locora can extract this archive into {installPath}.",
                false,
                new PackageExtractionWorkItem(
                    package.PackageId,
                    package.DisplayName,
                    version.Version,
                    sourceId,
                    cachePath,
                    version.ArchiveType,
                    installPath,
                    activePath,
                    version.ExecutablePath,
                    sha256,
                    ShouldExtractFromCache: true,
                    ShouldRefreshActivePath: activeAliasConfigured));
        }

        return new PackageExtractionPlan(
            "Unavailable",
            "Extraction waits for a cached artifact",
            $"Sync the archive into {cachePath} first, then Locora can extract it into {installPath}.",
            false,
            null);
    }

    private static IReadOnlyList<PackageCatalogSource> ResolveSourceCandidates(
        PackageSelectionRecord selection,
        PackageCatalogSnapshot catalog,
        IReadOnlyList<PackageCatalogSource> readySources)
    {
        if (!string.IsNullOrWhiteSpace(selection.SourceId))
        {
            var source = catalog.Sources.FirstOrDefault(candidate => candidate.Status.Id.Equals(selection.SourceId, StringComparison.OrdinalIgnoreCase));
            return source is null ? [] : [source];
        }

        return readySources;
    }

    private static string? ResolvePackageSourceId(PackageCatalogSnapshot catalog, string packageId)
    {
        return catalog.Sources
            .Where(source => source.Status.State.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.Status.Priority)
            .FirstOrDefault(source => source.Manifest?.Packages.Any(package =>
                package.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)) == true)
            ?.Status.Id;
    }

    private PackageManifestVersion? SelectVersion(PackageManifestPackage package, string requestedVersion)
    {
        PackageManifestVersion? selectedVersion = null;
        IReadOnlyList<int> selectedParts = [];
        var requestedVersionParts = ExtractVersionParts(requestedVersion);

        foreach (var version in package.Versions)
        {
            var candidateParts = ExtractVersionParts(version.Version);

            if (requestedVersionParts.Count > 0 && !MatchesRequestedVersion(candidateParts, requestedVersionParts))
            {
                continue;
            }

            if (selectedVersion is null || IsBetterCandidate(candidateParts, selectedParts, version.Version, selectedVersion.Version))
            {
                selectedVersion = version;
                selectedParts = candidateParts;
            }
        }

        if (selectedVersion is not null || requestedVersionParts.Count > 0)
        {
            return selectedVersion;
        }

        foreach (var version in package.Versions)
        {
            var candidateParts = ExtractVersionParts(version.Version);
            if (selectedVersion is null || IsBetterCandidate(candidateParts, selectedParts, version.Version, selectedVersion.Version))
            {
                selectedVersion = version;
                selectedParts = candidateParts;
            }
        }

        return selectedVersion;
    }

    private ArtifactSource ResolveArtifactSource(PackageManifestVersion version)
    {
        if (TryCreateRemoteUri(version.DownloadUri, out var downloadUri))
        {
            return new ArtifactSource(downloadUri.ToString(), null, downloadUri, GetFileName(downloadUri.LocalPath, version));
        }

        if (TryCreateRemoteUri(version.ArtifactPath, out var artifactUri))
        {
            return new ArtifactSource(artifactUri.ToString(), null, artifactUri, GetFileName(artifactUri.LocalPath, version));
        }

        var localPath = ResolveLocalPath(version.ArtifactPath);
        return new ArtifactSource(
            localPath,
            string.Equals(localPath, "(unset)", StringComparison.OrdinalIgnoreCase) ? null : localPath,
            null,
            GetFileName(localPath, version));
    }

    private string ResolveLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(unset)";
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, path));
    }

    private string BuildInstallPath(PackageManifestPackage package, PackageManifestVersion version)
    {
        var installRoot = ResolveLocalPath(package.InstallRootPath);
        if (installRoot.Equals("(unset)", StringComparison.OrdinalIgnoreCase))
        {
            return "(unresolved)";
        }

        if (string.IsNullOrWhiteSpace(version.RelativeInstallPath) ||
            version.RelativeInstallPath.Equals(".", StringComparison.OrdinalIgnoreCase))
        {
            return installRoot;
        }

        return Path.GetFullPath(Path.Combine(installRoot, version.RelativeInstallPath));
    }

    private string BuildActivePath(PackageManifestPackage package)
    {
        if (!package.SupportsActiveAlias || string.IsNullOrWhiteSpace(package.ActiveAliasPath))
        {
            return "(not configured)";
        }

        return ResolveLocalPath(package.ActiveAliasPath);
    }

    private string BuildCachePath(string packageId, string version, string fileName, string archiveType)
    {
        var extension = string.IsNullOrWhiteSpace(Path.GetExtension(fileName))
            ? $".{(string.IsNullOrWhiteSpace(archiveType) ? "pkg" : archiveType)}"
            : string.Empty;
        var safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? $"{packageId}-{version}{extension}"
            : fileName;

        return Path.Combine(_paths.PackageCacheRoot, packageId, version, safeFileName);
    }

    private static string BuildChildPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) ||
            rootPath.Equals("(unresolved)", StringComparison.OrdinalIgnoreCase) ||
            rootPath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase))
        {
            return rootPath;
        }

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Equals(".", StringComparison.OrdinalIgnoreCase))
        {
            return rootPath;
        }

        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetFileName(string? pathOrUriPath, PackageManifestVersion version)
    {
        var fileName = string.IsNullOrWhiteSpace(pathOrUriPath) ||
            pathOrUriPath.Equals("(unset)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Path.GetFileName(pathOrUriPath);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        var extension = string.IsNullOrWhiteSpace(version.ArchiveType) ? "pkg" : version.ArchiveType;
        return $"{version.Version}.{extension}";
    }

    private static bool TryCreateRemoteUri(string? value, out Uri uri)
    {
        uri = default!;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate is null ||
            (!candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private async Task<PackagesLockOptions> GetLockStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.PackagesLockSettingsFile))
        {
            return _lockOptions.CurrentValue;
        }

        try
        {
            await using var stream = File.OpenRead(_paths.PackagesLockSettingsFile);
            var document = await JsonSerializer.DeserializeAsync<PackagesLockDocument>(stream, ReadSerializerOptions, cancellationToken);
            return document?.LocoraPackagesLock ?? _lockOptions.CurrentValue;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read packages lock file {Path}; falling back to in-memory options.", _paths.PackagesLockSettingsFile);
            return _lockOptions.CurrentValue;
        }
    }

    private async Task WriteLockStateAsync(PackagesLockOptions lockState, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.PackagesLockSettingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_paths.PackagesLockSettingsFile);
        await JsonSerializer.SerializeAsync(
            stream,
            new PackagesLockDocument
            {
                LocoraPackagesLock = lockState
            },
            WriteSerializerOptions,
            cancellationToken);
    }

    private static string NormalizeArchiveType(string? archiveType)
    {
        return string.IsNullOrWhiteSpace(archiveType)
            ? "zip"
            : archiveType.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static string CombineDetails(string primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
        {
            return secondary;
        }

        if (string.IsNullOrWhiteSpace(secondary) ||
            primary.Equals(secondary, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        return $"{primary} {secondary}";
    }

    private void ExtractArchiveIntoInstallPath(string cachePath, string archiveType, string installPath, string executablePath)
    {
        if (!NormalizeArchiveType(archiveType).Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Archive type '{archiveType}' is not supported for extraction yet.");
        }

        var tempRoot = Path.Combine(_paths.TempRoot, "package-extract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(cachePath, tempRoot, overwriteFiles: true);
            var contentRoot = ResolveArchiveContentRoot(tempRoot, executablePath);

            if (!File.Exists(BuildChildPath(contentRoot, executablePath)))
            {
                throw new InvalidOperationException($"Archive did not contain the expected executable path '{executablePath}'.");
            }

            if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath, recursive: true);
            }

            CopyDirectory(contentRoot, installPath);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string ResolveArchiveContentRoot(string extractedRoot, string executablePath)
    {
        if (File.Exists(BuildChildPath(extractedRoot, executablePath)))
        {
            return extractedRoot;
        }

        var executableName = Path.GetFileName(executablePath.Replace('/', Path.DirectorySeparatorChar));
        var candidateRoots = Directory.EnumerateFiles(extractedRoot, executableName, SearchOption.AllDirectories)
            .Select(path => TryResolveContentRoot(path, executablePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        return candidateRoots.Length switch
        {
            1 => candidateRoots[0],
            > 1 => throw new InvalidOperationException($"Archive contained multiple candidate roots for executable path '{executablePath}'."),
            _ => throw new InvalidOperationException($"Archive did not contain the expected executable path '{executablePath}'.")
        };
    }

    private static string? TryResolveContentRoot(string executableFullPath, string executablePath)
    {
        var relativeSegments = executablePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directory = new FileInfo(executableFullPath).Directory;
        if (directory is null)
        {
            return null;
        }

        for (var index = relativeSegments.Length - 2; index >= 0; index--)
        {
            if (directory is null || !directory.Name.Equals(relativeSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var directory = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures and leave the original extraction error in logs.
        }
    }

    private void TryDeleteInstallRoot(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalizedPath = Path.GetFullPath(path);
            var binRoot = Path.GetFullPath(_paths.BinRoot);

            if (!normalizedPath.StartsWith(binRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipped deleting install path outside the portable bin root: {Path}", normalizedPath);
                return;
            }

            if (Directory.Exists(normalizedPath))
            {
                Directory.Delete(normalizedPath, recursive: true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to delete install path {Path}", path);
        }
    }

    private static void SyncActivePath(string installPath, string activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath) ||
            activePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase) ||
            activePath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Directory.Exists(activePath))
        {
            Directory.Delete(activePath, recursive: true);
        }

        CopyDirectory(installPath, activePath);
    }

    private void UpsertInstalledPackageRecord(List<InstalledPackageRecord> installedPackages, PackageExtractionWorkItem workItem)
    {
        for (var index = 0; index < installedPackages.Count; index++)
        {
            var record = installedPackages[index];
            if (!record.PackageId.Equals(workItem.PackageId, StringComparison.OrdinalIgnoreCase) ||
                record.Version.Equals(workItem.Version, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            installedPackages[index] = new InstalledPackageRecord
            {
                PackageId = record.PackageId,
                Version = record.Version,
                SourceId = record.SourceId,
                InstallPath = record.InstallPath,
                Status = "installed",
                InstalledAtUtc = record.InstalledAtUtc,
                Sha256 = record.Sha256
            };
        }

        var replacement = new InstalledPackageRecord
        {
            PackageId = workItem.PackageId,
            Version = workItem.Version,
            SourceId = workItem.SourceId,
            InstallPath = ToPortablePath(workItem.InstallPath),
            Status = string.IsNullOrWhiteSpace(workItem.ActivePath) || workItem.ActivePath.Equals("(not configured)", StringComparison.OrdinalIgnoreCase)
                ? "installed"
                : "active",
            InstalledAtUtc = DateTimeOffset.UtcNow,
            Sha256 = NormalizeSha256(workItem.Sha256)
        };

        var existingIndex = installedPackages.FindIndex(record =>
            record.PackageId.Equals(workItem.PackageId, StringComparison.OrdinalIgnoreCase) &&
            record.Version.Equals(workItem.Version, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            installedPackages[existingIndex] = replacement;
        }
        else
        {
            installedPackages.Add(replacement);
        }
    }

    private string ToPortablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) is false)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(_paths.AppRoot, path);
        return relativePath.StartsWith("..", StringComparison.Ordinal)
            ? path
            : relativePath.Replace('\\', '/');
    }

    private static bool IsRuntimePackage(PackageManifestPackage package)
    {
        return package.Kind.Equals("runtime", StringComparison.OrdinalIgnoreCase) ||
            package.Tags.Any(tag => tag.Equals("runtime", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsToolPackage(PackageManifestPackage package)
    {
        return package.Kind.Equals("tool", StringComparison.OrdinalIgnoreCase) ||
            package.Tags.Any(tag => tag.Equals("tool", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SortVersionLabels(IEnumerable<string> versions)
    {
        var items = versions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        items.Sort((left, right) => CompareVersionLabels(right, left));
        return items;
    }

    private static int CompareVersionLabels(string left, string right)
    {
        var leftParts = ExtractVersionParts(left);
        var rightParts = ExtractVersionParts(right);
        var count = Math.Max(leftParts.Count, rightParts.Count);

        for (var index = 0; index < count; index++)
        {
            var leftPart = index < leftParts.Count ? leftParts[index] : 0;
            var rightPart = index < rightParts.Count ? rightParts[index] : 0;
            var comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<int> ExtractVersionParts(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var match = VersionTextPattern.Match(value);
        if (!match.Success)
        {
            return [];
        }

        return match.Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var parsed) ? parsed : -1)
            .Where(part => part >= 0)
            .ToArray();
    }

    private static bool MatchesRequestedVersion(IReadOnlyList<int> candidateVersionParts, IReadOnlyList<int> requestedVersionParts)
    {
        if (requestedVersionParts.Count == 0 || candidateVersionParts.Count < requestedVersionParts.Count)
        {
            return requestedVersionParts.Count == 0;
        }

        for (var index = 0; index < requestedVersionParts.Count; index++)
        {
            if (candidateVersionParts[index] != requestedVersionParts[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBetterCandidate(
        IReadOnlyList<int> leftVersionParts,
        IReadOnlyList<int> rightVersionParts,
        string leftVersion,
        string rightVersion)
    {
        var maxLength = Math.Max(leftVersionParts.Count, rightVersionParts.Count);
        for (var index = 0; index < maxLength; index++)
        {
            var leftPart = index < leftVersionParts.Count ? leftVersionParts[index] : 0;
            var rightPart = index < rightVersionParts.Count ? rightVersionParts[index] : 0;

            if (leftPart != rightPart)
            {
                return leftPart > rightPart;
            }
        }

        return StringComparer.OrdinalIgnoreCase.Compare(leftVersion, rightVersion) > 0;
    }

    private ChecksumValidationResult ValidateCachedArtifact(string cachePath, string? expectedSha256)
    {
        try
        {
            var actualSha256 = ComputeSha256(cachePath);

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return new ChecksumValidationResult(
                    "Unverified",
                    null,
                    actualSha256,
                    "Artifact cached without expected checksum",
                    $"Computed SHA-256: {actualSha256}. Add this value to the manifest Sha256 field to enable checksum validation for {cachePath}.",
                    true);
            }

            if (actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ChecksumValidationResult(
                    "Verified",
                    expectedSha256,
                    actualSha256,
                    "Artifact cached and checksum verified",
                    $"SHA-256 verified for cached artifact {cachePath}.",
                    true);
            }

            return new ChecksumValidationResult(
                "Mismatch",
                expectedSha256,
                actualSha256,
                "Artifact checksum validation failed",
                $"Expected SHA-256 {expectedSha256} but computed {actualSha256} for {cachePath}. Replace the cache file or correct the manifest hash before installing this package.",
                false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to validate checksum for cached artifact {CachePath}", cachePath);

            return new ChecksumValidationResult(
                "Error",
                expectedSha256,
                null,
                "Checksum validation could not be completed",
                exception.Message,
                false);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string BuildSummary(
        int totalCount,
        int cachedCount,
        int pendingCount,
        int missingCount,
        int errorCount,
        int verifiedChecksumCount,
        int unverifiedChecksumCount,
        int checksumMismatchCount,
        int extractedCount,
        int pendingExtractionCount,
        int extractionErrorCount)
    {
        if (totalCount == 0)
        {
            return "No package downloads are configured.";
        }

        if (checksumMismatchCount > 0)
        {
            return $"{checksumMismatchCount} cached package artifacts failed checksum validation.";
        }

        if (errorCount > 0)
        {
            return $"{errorCount} package selections could not be resolved.";
        }

        if (missingCount > 0)
        {
            return $"{cachedCount}/{totalCount} package artifacts cached, {missingCount} still need a source artifact or download URL.";
        }

        if (pendingCount > 0)
        {
            return $"{cachedCount}/{totalCount} package artifacts cached, {pendingCount} are ready to sync.";
        }

        if (extractionErrorCount > 0)
        {
            return $"{cachedCount}/{totalCount} package artifacts cached, but {extractionErrorCount} extraction targets still need repair.";
        }

        if (pendingExtractionCount > 0)
        {
            return $"{cachedCount}/{totalCount} package artifacts cached, {pendingExtractionCount} are ready to extract into the runtime bin roots.";
        }

        if (unverifiedChecksumCount > 0)
        {
            return $"{cachedCount}/{totalCount} package artifacts cached, {extractedCount} extracted, {verifiedChecksumCount} checksum-verified and {unverifiedChecksumCount} still waiting for manifest hashes.";
        }

        return $"{cachedCount}/{totalCount} package artifacts cached, checksum-verified, and {extractedCount} extracted for runtime use.";
    }

    private string BuildDetails(
        int totalCount,
        int cachedCount,
        int pendingCount,
        int missingCount,
        int errorCount,
        int verifiedChecksumCount,
        int unverifiedChecksumCount,
        int checksumMismatchCount,
        int extractedCount,
        int pendingExtractionCount,
        int extractionErrorCount)
    {
        if (totalCount == 0)
        {
            return "Add entries to usr/config/packages.lock.json to stage runtime or tool archives into the package cache.";
        }

        if (checksumMismatchCount > 0)
        {
            return "At least one cached artifact does not match the Sha256 value declared in its package manifest. Replace the cache file or update the manifest hash before install or extraction.";
        }

        if (errorCount > 0)
        {
            return "Repair package sources or lock entries first so each selection resolves to a source and version before syncing downloads.";
        }

        if (missingCount > 0)
        {
            return $"Some selections still point at missing local artifacts. Add files under the expected artifact paths or set DownloadUri in the manifest, then sync again into {_paths.PackageCacheRoot}.";
        }

        if (pendingCount > 0)
        {
            return $"Run Install or Update Active Packages to copy or download pending artifacts and extract them into {_paths.BinRoot}, or use Sync Package Downloads when you only want to warm the cache.";
        }

        if (extractionErrorCount > 0)
        {
            return $"At least one cached archive could not be materialized into its install root under {_paths.BinRoot}. Repair the manifest archive type, refresh the active runtime path, or re-extract broken installs from cache.";
        }

        if (pendingExtractionCount > 0)
        {
            return $"Run Install or Update Active Packages to unpack cached artifacts from {_paths.PackageCacheRoot} into {_paths.BinRoot}, or use Extract Package Archives when the cache is already primed.";
        }

        if (unverifiedChecksumCount > 0)
        {
            return $"Some cached artifacts do not have manifest Sha256 values yet. Add Sha256 fields to the package manifest versions, then refresh so Locora can verify {_paths.PackageCacheRoot}.";
        }

        if (cachedCount > 0)
        {
            return $"All configured selections have a cached package artifact under {_paths.PackageCacheRoot}, {verifiedChecksumCount} artifacts passed SHA-256 validation, and {extractedCount} are already extracted under {_paths.BinRoot}.";
        }

        return $"Package downloads are idle. Cache root: {_paths.PackageCacheRoot}.";
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures and leave the original download error in logs.
        }
    }
}

public sealed record PackageDownloadManagerSnapshot(
    IReadOnlyList<PackageDownloadStatusSnapshot> Downloads,
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

public sealed record PackageDownloadStatusSnapshot(
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

public sealed record RuntimePackageManagerSnapshot(
    IReadOnlyList<RuntimePackageStatusSnapshot> Runtimes,
    int RuntimeCount,
    int InstalledCount,
    int ActiveCount,
    int SwitchableCount,
    int AttentionCount,
    string Summary,
    string Details);

public sealed record RuntimePackageStatusSnapshot(
    string PackageId,
    string DisplayName,
    string Family,
    string Kind,
    string State,
    string RequestedVersion,
    string ResolvedVersion,
    string ActiveVersion,
    string DefaultVersion,
    string SourceId,
    string Channel,
    string InstallRootPath,
    string ActivePath,
    string ExecutablePath,
    string Summary,
    string Details,
    IReadOnlyList<string> AvailableVersions,
    IReadOnlyList<string> InstalledVersions,
    bool SupportsSwitching,
    bool IsInstalled,
    bool IsActive);

public sealed record ToolPackageManagerSnapshot(
    IReadOnlyList<ToolPackageStatusSnapshot> Tools,
    int ToolCount,
    int SelectedCount,
    int InstalledCount,
    int ActiveCount,
    int AttentionCount,
    string Summary,
    string Details);

public sealed record ToolPackageStatusSnapshot(
    string PackageId,
    string DisplayName,
    string Family,
    string Kind,
    string State,
    string RequestedVersion,
    string ResolvedVersion,
    string ActiveVersion,
    string DefaultVersion,
    string SourceId,
    string Channel,
    string InstallRootPath,
    string ActivePath,
    string ExecutablePath,
    string Summary,
    string Details,
    IReadOnlyList<string> AvailableVersions,
    IReadOnlyList<string> InstalledVersions,
    IReadOnlyList<string> ProvidedCommands,
    bool IsSelected,
    bool IsInstalled,
    bool IsActive);

public sealed record PackageCompatibilityRequirement(
    string Key,
    string DisplayName,
    string PackageId,
    string RequestedVersion,
    string RelativeExecutablePath,
    string? RelativeStopExecutablePath);

public sealed record PackageCompatibilityManagerSnapshot(
    IReadOnlyList<PackageCompatibilityStatusSnapshot> Checks,
    int RequirementCount,
    int CompatibleCount,
    int AttentionCount,
    string Summary,
    string Details);

public sealed record PackageCompatibilityStatusSnapshot(
    string Key,
    string DisplayName,
    string PackageId,
    string RequestedVersion,
    string SelectedVersion,
    string ResolvedVersion,
    string State,
    string Summary,
    string Details,
    bool IsCompatible)
{
    public static PackageCompatibilityStatusSnapshot Attention(
        PackageCompatibilityRequirement requirement,
        string selectedVersion,
        string resolvedVersion,
        string summary,
        string details)
    {
        return new PackageCompatibilityStatusSnapshot(
            requirement.Key,
            requirement.DisplayName,
            requirement.PackageId,
            requirement.RequestedVersion,
            selectedVersion,
            resolvedVersion,
            "Attention",
            summary,
            details,
            false);
    }
}

internal sealed record PackageDownloadPlan(
    PackageDownloadManagerSnapshot Snapshot,
    PackageCatalogSnapshot Catalog,
    PackagesLockOptions LockState,
    IReadOnlyList<PackageDownloadWorkItem> DownloadWorkItems,
    IReadOnlyList<PackageExtractionWorkItem> ExtractionWorkItems);

internal sealed record PackageDownloadPlanItem(
    PackageDownloadStatusSnapshot Status,
    PackageDownloadWorkItem? DownloadWorkItem,
    PackageExtractionWorkItem? ExtractionWorkItem)
{
    public static PackageDownloadPlanItem Error(
        string id,
        string displayName,
        string requestedVersion,
        string sourceId,
        string summary,
        string details)
    {
        return new PackageDownloadPlanItem(
            new PackageDownloadStatusSnapshot(
                id,
                string.IsNullOrWhiteSpace(displayName) ? "Unknown package" : displayName,
                string.IsNullOrWhiteSpace(requestedVersion) ? "(default)" : requestedVersion,
                "Unavailable",
                string.IsNullOrWhiteSpace(sourceId) ? "(auto)" : sourceId,
                "Error",
                "Unavailable",
                null,
                null,
                "Unavailable",
                "(unresolved)",
                "(unresolved)",
                "(unresolved)",
                "(not configured)",
                summary,
                details,
                false,
                false),
            null,
            null);
    }
}

internal sealed record PackageDownloadWorkItem(
    string PackageId,
    string Version,
    string CachePath,
    string? LocalSourcePath,
    Uri? RemoteUri);

internal sealed record PackageExtractionWorkItem(
    string PackageId,
    string DisplayName,
    string Version,
    string SourceId,
    string CachePath,
    string ArchiveType,
    string InstallPath,
    string ActivePath,
    string ExecutablePath,
    string? Sha256,
    bool ShouldExtractFromCache,
    bool ShouldRefreshActivePath);

internal sealed record ArtifactSource(
    string DisplayValue,
    string? LocalPath,
    Uri? RemoteUri,
    string FileName);

internal sealed record PackageExtractionPlan(
    string State,
    string Summary,
    string Details,
    bool IsExtracted,
    PackageExtractionWorkItem? WorkItem);

internal sealed record ChecksumValidationResult(
    string State,
    string? ExpectedSha256,
    string? ActualSha256,
    string Summary,
    string Details,
    bool IsReady);

internal sealed record RuntimeCatalogPackage(
    PackageCatalogSource Source,
    PackageManifestPackage Package);

internal sealed record ToolCatalogPackage(
    PackageCatalogSource Source,
    PackageManifestPackage Package);

internal sealed record RuntimePackageSelection(
    PackageCatalogSource Source,
    PackageManifestPackage Package,
    PackageManifestVersion Version);
