using System.Text.RegularExpressions;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ProjectDiscoveryService
{
    private static readonly Regex InvalidSlugCharacters = new("[^a-z0-9-]+", RegexOptions.Compiled);
    private readonly IEnvironmentPaths _paths;
    private readonly ProjectDiscoveryOptions _options;
    private readonly ILogger<ProjectDiscoveryService> _logger;

    public ProjectDiscoveryService(
        IEnvironmentPaths paths,
        IOptions<ProjectDiscoveryOptions> options,
        ILogger<ProjectDiscoveryService> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<DiscoveredProject>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAutoDiscovery)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredProject>>(Array.Empty<DiscoveredProject>());
        }

        Directory.CreateDirectory(_paths.ProjectRoot);

        var ignoredNames = _options.IgnoredDirectoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                projects.Add(CreateProject(directory, name, usedSlugs));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to inspect project directory {Directory}", directory);
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveredProject>>(projects.OrderBy(project => project.Name).ToList());
    }

    private DiscoveredProject CreateProject(string projectPath, string name, HashSet<string> usedSlugs)
    {
        var slug = CreateUniqueSlug(name, usedSlugs);
        var framework = DetectFramework(projectPath);
        var runtime = DetectRuntime(projectPath, framework);
        var documentRoot = DetectDocumentRoot(projectPath, framework);
        var usesHttps = _options.DefaultScheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var url = $"{_options.DefaultScheme}://{slug}.{_options.DomainSuffix}";

        return new DiscoveredProject(
            name,
            slug,
            projectPath,
            documentRoot,
            url,
            runtime,
            framework,
            usesHttps);
    }

    private string DetectDocumentRoot(string projectPath, string framework)
    {
        var publicRoot = Path.Combine(projectPath, "public");
        if (Directory.Exists(publicRoot) && HasIndexFile(publicRoot))
        {
            return publicRoot;
        }

        if (framework.Equals("WordPress", StringComparison.OrdinalIgnoreCase))
        {
            return projectPath;
        }

        return projectPath;
    }

    private bool HasIndexFile(string directory)
    {
        return _options.IndexFileNames.Any(fileName => File.Exists(Path.Combine(directory, fileName)));
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
}
