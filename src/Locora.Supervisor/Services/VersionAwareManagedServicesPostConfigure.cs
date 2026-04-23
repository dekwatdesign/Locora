using System.Text.RegularExpressions;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class VersionAwareManagedServicesPostConfigure : IPostConfigureOptions<ManagedServicesOptions>
{
    private static readonly Regex VersionTextPattern = new(@"\d+(?:\.\d+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IEnvironmentPaths _paths;
    private readonly ILogger<VersionAwareManagedServicesPostConfigure> _logger;

    public VersionAwareManagedServicesPostConfigure(
        IEnvironmentPaths paths,
        ILogger<VersionAwareManagedServicesPostConfigure> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void PostConfigure(string? name, ManagedServicesOptions options)
    {
        for (var index = 0; index < options.Services.Count; index++)
        {
            options.Services[index] = ResolveDefinition(options.Services[index]);
        }
    }

    private ManagedServiceDefinition ResolveDefinition(ManagedServiceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.RelativeExecutablePath))
        {
            return definition;
        }

        var template = TryCreateTemplate(definition);
        if (template is null)
        {
            return definition;
        }

        if (TryResolveCurrentAlias(definition, template, out var aliasResolvedDefinition))
        {
            return aliasResolvedDefinition;
        }

        if (File.Exists(ResolvePath(definition.RelativeExecutablePath)))
        {
            return definition;
        }

        var candidates = EnumerateCandidates(definition, template);
        if (candidates.Count == 0)
        {
            return definition;
        }

        var requestedVersionParts = ExtractVersionParts(definition.Version);
        ResolvedVersionCandidate? selectedCandidate = null;

        foreach (var candidate in candidates)
        {
            if (requestedVersionParts.Count > 0 &&
                !MatchesRequestedVersion(candidate.VersionParts, requestedVersionParts))
            {
                continue;
            }

            if (selectedCandidate is null || IsBetterCandidate(candidate, selectedCandidate))
            {
                selectedCandidate = candidate;
            }
        }

        if (selectedCandidate is null)
        {
            var note = BuildMissingVersionNote(definition, template, candidates);
            _logger.LogWarning(
                "{Service} could not find an installed runtime matching requested version {RequestedVersion}. {Note}",
                definition.DisplayName,
                definition.Version,
                note);
            return CloneDefinition(definition, definition.Version, definition.RelativeExecutablePath, definition.RelativeStopExecutablePath, definition.RelativeWorkingDirectory, note);
        }

        _logger.LogInformation(
            "Resolved {Service} version {RequestedVersion} to installed runtime {ResolvedVersion}",
            definition.DisplayName,
            definition.Version,
            selectedCandidate.DisplayVersion);

        return CloneDefinition(
            definition,
            selectedCandidate.DisplayVersion,
            ToPortablePath(selectedCandidate.ExecutablePath),
            selectedCandidate.StopExecutablePath is null ? null : ToPortablePath(selectedCandidate.StopExecutablePath),
            selectedCandidate.WorkingDirectoryPath is null ? null : ToPortablePath(selectedCandidate.WorkingDirectoryPath),
            null);
    }

    private bool TryResolveCurrentAlias(
        ManagedServiceDefinition definition,
        VersionPathTemplate template,
        out ManagedServiceDefinition resolvedDefinition)
    {
        resolvedDefinition = definition;

        var currentDirectory = new DirectoryInfo(Path.Combine(template.ServiceRootPath, "current"));
        if (!currentDirectory.Exists)
        {
            return false;
        }

        DirectoryInfo? targetDirectory;

        try
        {
            targetDirectory = currentDirectory.ResolveLinkTarget(returnFinalTarget: true) as DirectoryInfo;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (targetDirectory is null || !targetDirectory.Exists)
        {
            return false;
        }

        var candidate = CreateCandidate(definition, template, targetDirectory);
        if (candidate is null)
        {
            return false;
        }

        _logger.LogInformation(
            "Resolved {Service} current runtime alias to installed version {ResolvedVersion}",
            definition.DisplayName,
            candidate.DisplayVersion);

        resolvedDefinition = CloneDefinition(
            definition,
            candidate.DisplayVersion,
            ToPortablePath(candidate.ExecutablePath),
            candidate.StopExecutablePath is null ? null : ToPortablePath(candidate.StopExecutablePath),
            candidate.WorkingDirectoryPath is null ? null : ToPortablePath(candidate.WorkingDirectoryPath),
            null);
        return true;
    }

    private List<ResolvedVersionCandidate> EnumerateCandidates(ManagedServiceDefinition definition, VersionPathTemplate template)
    {
        var candidates = new List<ResolvedVersionCandidate>();
        var serviceRoot = new DirectoryInfo(template.ServiceRootPath);

        if (!serviceRoot.Exists)
        {
            return candidates;
        }

        foreach (var directory in serviceRoot.EnumerateDirectories())
        {
            if (directory.Name.Equals("current", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = CreateCandidate(definition, template, directory);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static ResolvedVersionCandidate? CreateCandidate(
        ManagedServiceDefinition definition,
        VersionPathTemplate template,
        DirectoryInfo directory)
    {
        var executablePath = BuildCandidatePath(directory.FullName, template.ExecutableSuffix);
        if (!File.Exists(executablePath))
        {
            return null;
        }

        string? stopExecutablePath = null;
        if (template.StopExecutableSuffix is not null)
        {
            stopExecutablePath = BuildCandidatePath(directory.FullName, template.StopExecutableSuffix);
            if (!File.Exists(stopExecutablePath))
            {
                return null;
            }
        }

        string? workingDirectoryPath = null;
        if (template.WorkingDirectorySuffix is not null)
        {
            workingDirectoryPath = BuildCandidatePath(directory.FullName, template.WorkingDirectorySuffix);
            if (!Directory.Exists(workingDirectoryPath))
            {
                workingDirectoryPath = Path.GetDirectoryName(executablePath);
            }
        }

        var versionParts = ExtractVersionParts(directory.Name);
        var displayVersion = versionParts.Count > 0
            ? string.Join('.', versionParts)
            : directory.Name;

        return new ResolvedVersionCandidate(
            directory.Name,
            displayVersion,
            versionParts,
            executablePath,
            stopExecutablePath,
            workingDirectoryPath ?? Path.GetDirectoryName(executablePath));
    }

    private VersionPathTemplate? TryCreateTemplate(ManagedServiceDefinition definition)
    {
        if (!TrySplitPath(definition.RelativeExecutablePath, out var serviceRootRelativePath, out var executableSuffix))
        {
            return null;
        }

        string? stopExecutableSuffix = null;
        if (!string.IsNullOrWhiteSpace(definition.RelativeStopExecutablePath))
        {
            if (!TrySplitPath(definition.RelativeStopExecutablePath, out var stopRootRelativePath, out stopExecutableSuffix) ||
                !serviceRootRelativePath.Equals(stopRootRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        string? workingDirectorySuffix = null;
        if (!string.IsNullOrWhiteSpace(definition.RelativeWorkingDirectory))
        {
            if (!TrySplitPath(definition.RelativeWorkingDirectory, out var workingRootRelativePath, out workingDirectorySuffix) ||
                !serviceRootRelativePath.Equals(workingRootRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                workingDirectorySuffix = null;
            }
        }

        return new VersionPathTemplate(serviceRootRelativePath, ResolvePath(serviceRootRelativePath), executableSuffix, stopExecutableSuffix, workingDirectorySuffix);
    }

    private static bool TrySplitPath(string path, out string serviceRootRelativePath, out string suffix)
    {
        serviceRootRelativePath = string.Empty;
        suffix = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var segments = path
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 3 || !segments[0].Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        serviceRootRelativePath = string.Join('/', segments[..2]);
        suffix = string.Join('/', segments[3..]);
        return true;
    }

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, path));
    }

    private string ToPortablePath(string path)
    {
        var relativePath = Path.GetRelativePath(_paths.AppRoot, path);
        return relativePath.StartsWith("..", StringComparison.Ordinal)
            ? path
            : relativePath.Replace('\\', '/');
    }

    private static string BuildCandidatePath(string directoryPath, string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return directoryPath;
        }

        return Path.Combine(directoryPath, suffix.Replace('/', Path.DirectorySeparatorChar));
    }

    private static ManagedServiceDefinition CloneDefinition(
        ManagedServiceDefinition source,
        string version,
        string executablePath,
        string? stopExecutablePath,
        string? workingDirectoryPath,
        string? versionResolutionNote)
    {
        return new ManagedServiceDefinition
        {
            Key = source.Key,
            DisplayName = source.DisplayName,
            Kind = source.Kind,
            Version = version,
            VersionResolutionNote = versionResolutionNote,
            RelativeExecutablePath = executablePath,
            RelativeStopExecutablePath = stopExecutablePath,
            RelativeWorkingDirectory = workingDirectoryPath,
            Arguments = [.. source.Arguments],
            StopArguments = [.. source.StopArguments],
            EnvironmentVariables = new Dictionary<string, string>(source.EnvironmentVariables, StringComparer.OrdinalIgnoreCase),
            Port = source.Port,
            AutoStart = source.AutoStart,
            RestartOnCrash = source.RestartOnCrash,
            RestartBackoffMs = source.RestartBackoffMs,
            MaxRestartAttempts = source.MaxRestartAttempts,
            RestartWindowMs = source.RestartWindowMs,
            StartTimeoutMs = source.StartTimeoutMs,
            StopTimeoutMs = source.StopTimeoutMs
        };
    }

    private static string BuildMissingVersionNote(
        ManagedServiceDefinition definition,
        VersionPathTemplate template,
        IReadOnlyList<ResolvedVersionCandidate> candidates)
    {
        var availableVersions = candidates
            .Select(candidate => candidate.DisplayVersion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5);

        return $"Configured {definition.DisplayName} for version {definition.Version}, but no installed runtime under {template.ServiceRootRelativePath} matched. Available versions: {string.Join(", ", availableVersions)}.";
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

    private static bool IsBetterCandidate(ResolvedVersionCandidate left, ResolvedVersionCandidate right)
    {
        var maxLength = Math.Max(left.VersionParts.Count, right.VersionParts.Count);
        for (var index = 0; index < maxLength; index++)
        {
            var leftPart = index < left.VersionParts.Count ? left.VersionParts[index] : 0;
            var rightPart = index < right.VersionParts.Count ? right.VersionParts[index] : 0;

            if (leftPart != rightPart)
            {
                return leftPart > rightPart;
            }
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.DirectoryName, right.DirectoryName) > 0;
    }

    private sealed record VersionPathTemplate(
        string ServiceRootRelativePath,
        string ServiceRootPath,
        string ExecutableSuffix,
        string? StopExecutableSuffix,
        string? WorkingDirectorySuffix);

    private sealed record ResolvedVersionCandidate(
        string DirectoryName,
        string DisplayVersion,
        IReadOnlyList<int> VersionParts,
        string ExecutablePath,
        string? StopExecutablePath,
        string? WorkingDirectoryPath);
}
