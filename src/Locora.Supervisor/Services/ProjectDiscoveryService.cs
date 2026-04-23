using System.Text.Json;
using System.Text.RegularExpressions;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class ProjectDiscoveryService
{
    private static readonly Regex InvalidSlugCharacters = new("[^a-z0-9-]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private const string ProjectMetadataFileName = ".locora.json";
    private static readonly ProjectDiscoveryOptions DefaultOptions = new();
    private readonly IEnvironmentPaths _paths;
    private readonly ILogger<ProjectDiscoveryService> _logger;

    public ProjectDiscoveryService(
        IEnvironmentPaths paths,
        ILogger<ProjectDiscoveryService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var options = GetCurrentOptions();
        if (!options.EnableAutoDiscovery)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredProject>>(Array.Empty<DiscoveredProject>());
        }

        Directory.CreateDirectory(_paths.ProjectRoot);

        var ignoredNames = options.IgnoredDirectoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projects = new List<DiscoveredProject>();
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in EnumerateProjectDirectories(options, ignoredNames))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                projects.Add(CreateProject(directory, name, usedSlugs, options));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to inspect project directory {Directory}", directory);
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveredProject>>(projects.OrderBy(project => project.Name).ToList());
    }

    private IEnumerable<string> EnumerateProjectDirectories(ProjectDiscoveryOptions options, ISet<string> ignoredNames)
    {
        var yieldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(_paths.ProjectRoot))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name) || ignoredNames.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(directory);
            if (yieldedPaths.Add(fullPath))
            {
                yield return fullPath;
            }
        }

        foreach (var projectOverride in options.ProjectOverrides)
        {
            if (!TryResolveProjectOverrideDirectory(projectOverride.Path, out var overridePath) ||
                !Directory.Exists(overridePath) ||
                !yieldedPaths.Add(overridePath))
            {
                continue;
            }

            yield return overridePath;
        }
    }

    private bool TryResolveProjectOverrideDirectory(string? candidate, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            resolvedPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(_paths.ProjectRoot, candidate));
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Ignoring invalid project override path '{Path}'.", candidate);
            return false;
        }
    }

    private DiscoveredProject CreateProject(string projectPath, string name, HashSet<string> usedSlugs, ProjectDiscoveryOptions options)
    {
        var slug = CreateUniqueSlug(name, usedSlugs);
        var metadata = LoadProjectMetadata(projectPath);
        var projectOverride = ResolveProjectOverride(projectPath, name, slug, options);
        var appliedOverrides = new List<string>();
        var framework = ResolveTextOverride(projectOverride?.Framework, metadata?.Framework, DetectFramework(projectPath), "framework", appliedOverrides);
        var runtime = ResolveTextOverride(projectOverride?.Runtime, metadata?.Runtime, DetectRuntime(projectPath, framework), "runtime", appliedOverrides);
        var documentRoot = ResolveDocumentRoot(projectPath, framework, metadata, projectOverride, options, appliedOverrides);
        var scheme = ResolveScheme(projectPath, metadata, projectOverride, options, appliedOverrides);
        var host = ResolveHost(projectPath, slug, metadata, projectOverride, options, appliedOverrides);
        var usesHttps = scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var url = $"{scheme}://{host}";
        var description = ResolveTextOverride(
            projectOverride?.Description,
            metadata?.Description,
            string.Empty,
            "description",
            appliedOverrides);
        var tags = NormalizeTags(metadata, projectOverride, appliedOverrides);

        return new DiscoveredProject(
            name,
            slug,
            projectPath,
            documentRoot,
            url,
            runtime,
            framework,
            description,
            tags,
            usesHttps,
            CreateOverrideSummary(appliedOverrides));
    }

    private string ResolveHost(
        string projectPath,
        string slug,
        ProjectMetadata? metadata,
        ProjectOverrideDefinition? projectOverride,
        ProjectDiscoveryOptions options,
        List<string> appliedOverrides)
    {
        var defaultHost = $"{slug}.{options.DomainSuffix}";
        var domainOverride = FirstNonWhiteSpace(projectOverride?.Domain, metadata?.Domain);

        if (string.IsNullOrWhiteSpace(domainOverride))
        {
            return defaultHost;
        }

        if (TryNormalizeDomain(domainOverride, out var normalizedDomain))
        {
            appliedOverrides.Add("domain");
            return normalizedDomain;
        }

        _logger.LogWarning(
            "Ignoring invalid Locora domain override '{Domain}' in {MetadataPath}",
            domainOverride,
            Path.Combine(projectPath, ProjectMetadataFileName));

        return defaultHost;
    }

    private ProjectMetadata? LoadProjectMetadata(string projectPath)
    {
        var metadataPath = Path.Combine(projectPath, ProjectMetadataFileName);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<ProjectMetadata>(content, SerializerOptions);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load Locora project metadata from {MetadataPath}", metadataPath);
            return null;
        }
    }

    private static string ResolveTextOverride(
        string? primary,
        string? secondary,
        string fallback,
        string overrideName,
        List<string> appliedOverrides)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            appliedOverrides.Add(overrideName);
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            appliedOverrides.Add(overrideName);
            return secondary.Trim();
        }

        return fallback;
    }

    private static IReadOnlyList<string> NormalizeTags(
        ProjectMetadata? metadata,
        ProjectOverrideDefinition? projectOverride,
        List<string> appliedOverrides)
    {
        var tags = EnumerateTagCandidates(metadata?.Tags)
            .Concat(EnumerateTagCandidates(projectOverride?.Tags))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tags.Length > 0)
        {
            appliedOverrides.Add("tags");
        }

        return tags;
    }

    private static IEnumerable<string> EnumerateTagCandidates(JsonElement? tags)
    {
        if (tags is null || tags.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            yield break;
        }

        var value = tags.Value;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in value.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    yield return tag.GetString() ?? string.Empty;
                }
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            foreach (var tag in (value.GetString() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return tag;
            }
        }
    }

    private string ResolveDocumentRoot(
        string projectPath,
        string framework,
        ProjectMetadata? metadata,
        ProjectOverrideDefinition? projectOverride,
        ProjectDiscoveryOptions options,
        List<string> appliedOverrides)
    {
        var documentRootOverride = FirstNonWhiteSpace(projectOverride?.DocumentRoot, metadata?.DocumentRoot);
        if (!string.IsNullOrWhiteSpace(documentRootOverride))
        {
            var resolvedPath = ResolveProjectRelativePath(projectPath, documentRootOverride);
            if (Directory.Exists(resolvedPath) && IsWithinProject(projectPath, resolvedPath))
            {
                appliedOverrides.Add("document root");
                return resolvedPath;
            }

            _logger.LogWarning(
                "Ignoring invalid Locora document root override '{DocumentRoot}' for project {ProjectPath}. The path must be an existing directory inside the project.",
                documentRootOverride,
                projectPath);
        }

        return DetectDocumentRoot(projectPath, framework, options);
    }

    private string DetectDocumentRoot(string projectPath, string framework, ProjectDiscoveryOptions options)
    {
        var publicRoot = Path.Combine(projectPath, "public");
        if (Directory.Exists(publicRoot) && HasIndexFile(publicRoot, options))
        {
            return publicRoot;
        }

        if (framework.Equals("WordPress", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        return projectPath;
    }

    private string ResolveScheme(
        string projectPath,
        ProjectMetadata? metadata,
        ProjectOverrideDefinition? projectOverride,
        ProjectDiscoveryOptions options,
        List<string> appliedOverrides)
    {
        var schemeOverride = FirstNonWhiteSpace(projectOverride?.Scheme, metadata?.Scheme);
        if (string.IsNullOrWhiteSpace(schemeOverride))
        {
            return options.DefaultScheme;
        }

        var scheme = schemeOverride.Trim().ToLowerInvariant();
        if (scheme is "http" or "https")
        {
            appliedOverrides.Add("scheme");
            return scheme;
        }

        _logger.LogWarning(
            "Ignoring invalid Locora scheme override '{Scheme}' for project {ProjectPath}. Use http or https.",
            schemeOverride,
            projectPath);
        return options.DefaultScheme;
    }

    private ProjectOverrideDefinition? ResolveProjectOverride(
        string projectPath,
        string name,
        string slug,
        ProjectDiscoveryOptions options)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        return options.ProjectOverrides.FirstOrDefault(projectOverride =>
            MatchesProjectOverrideKey(projectOverride.Key, slug, name) ||
            MatchesProjectOverrideKey(projectOverride.Name, slug, name) ||
            MatchesProjectOverridePath(projectOverride.Path, fullProjectPath, _paths.ProjectRoot));
    }

    private static bool MatchesProjectOverrideKey(string? candidate, string slug, string name)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = candidate.Trim();
        return value.Equals(slug, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesProjectOverridePath(string? candidate, string fullProjectPath, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var candidatePath = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(projectRoot, candidate));
            return candidatePath.Equals(fullProjectPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveProjectRelativePath(string projectPath, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectPath, path));
    }

    private static bool IsWithinProject(string projectPath, string candidatePath)
    {
        var root = EnsureTrailingDirectorySeparator(Path.GetFullPath(projectPath));
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string CreateOverrideSummary(IReadOnlyCollection<string> appliedOverrides)
    {
        return appliedOverrides.Count == 0
            ? "Overrides: none"
            : $"Overrides: {string.Join(", ", appliedOverrides.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}";
    }

    private bool HasIndexFile(string directory, ProjectDiscoveryOptions options)
    {
        return options.IndexFileNames.Any(fileName => File.Exists(Path.Combine(directory, fileName)));
    }

    private static string DetectFramework(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "artisan")))
        {
            return "Laravel";
        }

        if (File.Exists(Path.Combine(projectPath, "wp-config.php")) ||
            Directory.Exists(Path.Combine(projectPath, "wp-content")))
        {
            return "WordPress";
        }

        if (File.Exists(Path.Combine(projectPath, "symfony.lock")))
        {
            return "Symfony";
        }

        if (File.Exists(Path.Combine(projectPath, "next.config.js")) ||
            File.Exists(Path.Combine(projectPath, "next.config.mjs")) ||
            Directory.Exists(Path.Combine(projectPath, ".next")))
        {
            return "Next.js";
        }

        if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
            File.Exists(Path.Combine(projectPath, "requirements.txt")))
        {
            return "Python";
        }

        if (File.Exists(Path.Combine(projectPath, "pom.xml")) ||
            File.Exists(Path.Combine(projectPath, "build.gradle")) ||
            File.Exists(Path.Combine(projectPath, "build.gradle.kts")) ||
            File.Exists(Path.Combine(projectPath, "mvnw")) ||
            File.Exists(Path.Combine(projectPath, "gradlew")))
        {
            return "Java";
        }

        if (File.Exists(Path.Combine(projectPath, "composer.json")))
        {
            return "Composer";
        }

        if (File.Exists(Path.Combine(projectPath, "package.json")))
        {
            return "Node";
        }

        if (File.Exists(Path.Combine(projectPath, "index.php")))
        {
            return "PHP";
        }

        return "Static";
    }

    private static string DetectRuntime(string projectPath, string framework)
    {
        if (framework is "Laravel" or "WordPress" or "Symfony" or "Composer" or "PHP")
        {
            return "PHP";
        }

        if ((framework is "Next.js" or "Node") || File.Exists(Path.Combine(projectPath, "package.json")))
        {
            return "Node.js";
        }

        if (framework == "Python")
        {
            return "Python";
        }

        if (framework == "Java")
        {
            return "Java";
        }

        return "Static";
    }

    private static string CreateUniqueSlug(string name, HashSet<string> usedSlugs)
    {
        var normalized = name.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        normalized = InvalidSlugCharacters.Replace(normalized, "-").Trim('-');
        var baseSlug = string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
        var slug = baseSlug;
        var suffix = 2;

        while (!usedSlugs.Add(slug))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }

    private static bool TryNormalizeDomain(string candidate, out string host)
    {
        var value = candidate.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                host = string.Empty;
                return false;
            }

            if (!uri.IsDefaultPort || uri.AbsolutePath is not ("/" or "") || !string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
            {
                host = string.Empty;
                return false;
            }

            value = uri.Host;
        }

        if (value.IndexOfAny(new[] { '/', '\\', '?', '#', ':' }) >= 0 || value.Any(char.IsWhiteSpace))
        {
            host = string.Empty;
            return false;
        }

        value = value.Trim('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || Uri.CheckHostName(value) == UriHostNameType.Unknown)
        {
            host = string.Empty;
            return false;
        }

        host = value;
        return true;
    }

    private ProjectDiscoveryOptions GetCurrentOptions()
    {
        var settingsPath = _paths.ProjectsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return NormalizeOptions(null);
        }

        try
        {
            var content = File.ReadAllText(settingsPath);
            var document = JsonSerializer.Deserialize<ProjectSettingsDocument>(content, SerializerOptions);
            return NormalizeOptions(document?.LocoraProjects);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load project discovery settings from {SettingsPath}", settingsPath);
            return NormalizeOptions(null);
        }
    }

    private ProjectDiscoveryOptions NormalizeOptions(ProjectDiscoveryOptions? options)
    {
        return new ProjectDiscoveryOptions
        {
            EnableAutoDiscovery = options?.EnableAutoDiscovery ?? DefaultOptions.EnableAutoDiscovery,
            GenerateNginxVHosts = options?.GenerateNginxVHosts ?? DefaultOptions.GenerateNginxVHosts,
            GenerateApacheVHosts = options?.GenerateApacheVHosts ?? DefaultOptions.GenerateApacheVHosts,
            GenerateHostsPreview = options?.GenerateHostsPreview ?? DefaultOptions.GenerateHostsPreview,
            DomainSuffix = NormalizeDomainSuffix(options?.DomainSuffix),
            DefaultScheme = NormalizeDefaultScheme(options?.DefaultScheme),
            IndexFileNames = NormalizeNameList(options?.IndexFileNames, DefaultOptions.IndexFileNames),
            IgnoredDirectoryNames = NormalizeNameList(options?.IgnoredDirectoryNames, DefaultOptions.IgnoredDirectoryNames),
            ProjectOverrides = NormalizeProjectOverrides(options?.ProjectOverrides)
        };
    }

    private string NormalizeDomainSuffix(string? candidate)
    {
        var value = (candidate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultOptions.DomainSuffix;
        }

        if (value.StartsWith('.'))
        {
            value = value.TrimStart('.');
        }

        if (TryNormalizeDomain(value, out var normalizedDomain))
        {
            return normalizedDomain;
        }

        _logger.LogWarning(
            "Project discovery config has an invalid DomainSuffix value '{DomainSuffix}'. Falling back to {Fallback}.",
            candidate,
            DefaultOptions.DomainSuffix);

        return DefaultOptions.DomainSuffix;
    }

    private static string NormalizeDefaultScheme(string? candidate)
    {
        return string.Equals(candidate, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
    }

    private static List<string> NormalizeNameList(IEnumerable<string>? candidates, IEnumerable<string> fallback)
    {
        var normalized = candidates?
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized is { Count: > 0 } ? normalized : fallback.ToList();
    }

    private static List<ProjectOverrideDefinition> NormalizeProjectOverrides(IEnumerable<ProjectOverrideDefinition>? overrides)
    {
        return overrides?
            .Where(projectOverride =>
                !string.IsNullOrWhiteSpace(projectOverride.Key) ||
                !string.IsNullOrWhiteSpace(projectOverride.Name) ||
                !string.IsNullOrWhiteSpace(projectOverride.Path))
            .ToList() ?? [];
    }

    private sealed record ProjectMetadata(
        string? Domain,
        string? Scheme,
        string? DocumentRoot,
        string? Runtime,
        string? Framework,
        string? Description,
        JsonElement Tags);

    private sealed record ProjectSettingsDocument(ProjectDiscoveryOptions? LocoraProjects);
}
