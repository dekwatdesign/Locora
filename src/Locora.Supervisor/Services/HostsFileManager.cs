using Locora.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class HostsFileManager
{
    private const string BeginMarker = "# BEGIN LOCORA MANAGED HOSTS";
    private const string EndMarker = "# END LOCORA MANAGED HOSTS";
    private readonly IEnvironmentPaths _paths;
    private readonly ILogger<HostsFileManager> _logger;

    public HostsFileManager(IEnvironmentPaths paths, ILogger<HostsFileManager> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string HostsFilePath => ResolveHostsFilePath();

    public string PreviewPath => Path.Combine(_paths.ConfigRoot, "hosts.locora.generated");

    public string BackupRoot => Path.Combine(_paths.ConfigRoot, "hosts.backups");

    public async Task ApplyPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PreviewPath))
        {
            throw new FileNotFoundException("Generated hosts preview was not found. Refresh projects first.", PreviewPath);
        }

        var previewEntries = await ReadPreviewEntriesAsync(cancellationToken);
        var hostsPath = HostsFilePath;
        var originalContent = File.Exists(hostsPath)
            ? await File.ReadAllTextAsync(hostsPath, cancellationToken)
            : string.Empty;

        CreateBackup(hostsPath, originalContent);

        var unmanagedContent = RemoveManagedBlock(originalContent).TrimEnd();
        var nextContent = string.IsNullOrWhiteSpace(unmanagedContent)
            ? CreateManagedBlock(previewEntries)
            : unmanagedContent + Environment.NewLine + Environment.NewLine + CreateManagedBlock(previewEntries);

        await File.WriteAllTextAsync(hostsPath, nextContent + Environment.NewLine, cancellationToken);
        _logger.LogInformation("Applied {Count} Locora hosts entries to {Path}", previewEntries.Count, hostsPath);
    }

    public async Task RollbackLatestAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupRoot);

        var latestBackup = Directory.EnumerateFiles(BackupRoot, "hosts.*.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestBackup is null)
        {
            throw new FileNotFoundException("No Locora hosts backup is available for rollback.", BackupRoot);
        }

        var backupContent = await File.ReadAllTextAsync(latestBackup, cancellationToken);
        await File.WriteAllTextAsync(HostsFilePath, backupContent, cancellationToken);
        _logger.LogInformation("Rolled hosts file back from {Backup}", latestBackup);
    }

    private async Task<IReadOnlyList<string>> ReadPreviewEntriesAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(PreviewPath, cancellationToken);
        return lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CreateBackup(string hostsPath, string originalContent)
    {
        Directory.CreateDirectory(BackupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(hostsPath) ?? _paths.ConfigRoot);

        var backupPath = Path.Combine(BackupRoot, $"hosts.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak");
        File.WriteAllText(backupPath, originalContent);
        _logger.LogInformation("Created hosts backup at {Backup}", backupPath);
    }

    private static string CreateManagedBlock(IReadOnlyList<string> entries)
    {
        return string.Join(
            Environment.NewLine,
            new[] { BeginMarker }
                .Concat(entries)
                .Concat(new[] { EndMarker }));
    }

    private static string RemoveManagedBlock(string content)
    {
        using var reader = new StringReader(content);
        var output = new List<string>();
        var insideManagedBlock = false;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();

            if (trimmed.Equals(BeginMarker, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = true;
                continue;
            }

            if (trimmed.Equals(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                insideManagedBlock = false;
                continue;
            }

            if (!insideManagedBlock)
            {
                output.Add(line);
            }
        }

        return string.Join(Environment.NewLine, output);
    }

    private static string ResolveHostsFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LOCORA_HOSTS_FILE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(systemDirectory))
        {
            return Path.Combine(systemDirectory, "drivers", "etc", "hosts");
        }

        return Path.Combine(
            Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
            "System32",
            "drivers",
            "etc",
            "hosts");
    }
}
