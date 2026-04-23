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

        foreach (var directory in Directory.EnumerateDirectories(_paths.ProjectRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name) || ignoredNames.Contains(name) || name.StartsWith('.'))
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

    private DiscoveredProject CreateProject(string projectPath, string name, HashSet<string> usedSlugs, ProjectDiscoveryOptions options)
    {
        var slug = CreateUniqueSlug(name, usedSlugs);
        var metadata = LoadProjectMetadata(projectPath);
        var host = ResolveHost(projectPath, slug, metadata, options);
        var framework = DetectFramework(projectPath);
        var runtime = DetectRuntime(projectPath, framework);
        var documentRoot = DetectDocumentRoot(projectPath, framework, options);
        var usesHttps = options.DefaultScheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var url = $"{options.DefaultScheme}://{host}";

        return new DiscoveredProject(
            name,
            slug,
            projectPath,
            documentRoot,
            url,
            runtime,
            framework,
            NormalizeDescription(metadata?.Description),
            NormalizeTags(metadata),
            usesHttps);
    }

    private string ResolveHost(string projectPath, string slug, ProjectMetadata? metadata, ProjectDiscoveryOptions options)
    {
        var defaultHost = $"{slug}.{options.DomainSuffix}";

        if (string.IsNullOrWhiteSpace(metadata?.Domain))
        {
            return defaultHost;
        }

        if (TryNormalizeDomain(metadata.Domain, out var normalizedDomain))
        {
            return normalizedDomain;
        }

        _logger.LogWarning(
            "Ignoring invalid Locora domain override '{Domain}' in {MetadataPath}",
            metadata.Domain,
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

    private static string NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
    }

    private static IReadOnlyList<string> NormalizeTags(ProjectMetadata? metadata)
    {
        if (metadata is null || metadata.Tags.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        return EnumerateTagCandidates(metadata.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateTagCandidates(JsonElement tags)
    {
        if (tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    yield return tag.GetString() ?? string.Empty;
                }
            }

            yield break;
        }

        if (tags.ValueKind == JsonValueKind.String)
        {
            foreach (var tag in (tags.GetString() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return tag;
            }
        }
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
            IgnoredDirectoryNames = NormalizeNameList(options?.IgnoredDirectoryNames, DefaultOptions.IgnoredDirectoryNames)
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

    private sealed record ProjectMetadata(string? Domain, string? Description, JsonElement Tags);

    private sealed record ProjectSettingsDocument(ProjectDiscoveryOptions? LocoraProjects);
}
