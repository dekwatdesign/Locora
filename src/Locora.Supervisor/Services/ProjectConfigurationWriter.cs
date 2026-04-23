using System.Text;
using System.Text.Json;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ProjectConfigurationWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly ProjectDiscoveryOptions DefaultProjectOptions = new();
    private readonly IEnvironmentPaths _paths;
    private readonly ManagedServicesOptions _managedServices;
    private readonly LocalSslService _localSslService;
    private readonly ILogger<ProjectConfigurationWriter> _logger;

    public ProjectConfigurationWriter(
        IEnvironmentPaths paths,
        IOptions<ManagedServicesOptions> managedServices,
        LocalSslService localSslService,
        ILogger<ProjectConfigurationWriter> logger)
    {
        _paths = paths;
        _managedServices = managedServices.Value;
        _localSslService = localSslService;
        _logger = logger;
    }

    public async Task GenerateAsync(IReadOnlyList<DiscoveredProject> projects, CancellationToken cancellationToken = default)
    {
        var options = GetCurrentOptions();
        var domainPlan = CreateDomainPlan(projects);

        foreach (var collision in domainPlan.Collisions)
        {
            _logger.LogWarning(
                "Skipping domain artifacts for host {Host} because multiple projects claim it: {Projects}",
                collision.Host,
                string.Join(", ", collision.Projects.Select(project => project.Name)));
        }

        if (options.GenerateNginxVHosts)
        {
            await GenerateNginxVHostsAsync(domainPlan.UniqueProjects, cancellationToken);
        }

        if (options.GenerateApacheVHosts)
        {
            await GenerateApacheVHostsAsync(domainPlan.UniqueProjects, cancellationToken);
        }

        if (options.GenerateHostsPreview)
        {
            await GenerateHostsPreviewAsync(domainPlan, cancellationToken);
        }
    }

    private async Task GenerateNginxVHostsAsync(IReadOnlyList<DiscoveredProject> projects, CancellationToken cancellationToken)
    {
        var vhostsRoot = Path.Combine(_paths.ConfigRoot, "nginx", "vhosts");
        Directory.CreateDirectory(vhostsRoot);

        foreach (var staleFile in Directory.EnumerateFiles(vhostsRoot, "*.conf"))
        {
            File.Delete(staleFile);
        }

        foreach (var project in projects)
        {
            var vhostPath = Path.Combine(vhostsRoot, $"{project.Slug}.conf");
            await File.WriteAllTextAsync(vhostPath, CreateNginxVHost(project), cancellationToken);
            _logger.LogInformation("Generated Nginx vhost for {Project} at {Path}", project.Name, vhostPath);
        }
    }

    private async Task GenerateApacheVHostsAsync(IReadOnlyList<DiscoveredProject> projects, CancellationToken cancellationToken)
    {
        var vhostsRoot = Path.Combine(_paths.ConfigRoot, "apache", "vhosts");
        Directory.CreateDirectory(vhostsRoot);

        foreach (var staleFile in Directory.EnumerateFiles(vhostsRoot, "*.conf"))
        {
            File.Delete(staleFile);
        }

        var apachePort = ResolveApachePort();
        if (apachePort is null)
        {
            _logger.LogWarning("Skipping Apache vhost generation because no Apache service definition is configured.");
            return;
        }

        foreach (var project in projects)
        {
            var vhostPath = Path.Combine(vhostsRoot, $"{project.Slug}.conf");
            await File.WriteAllTextAsync(vhostPath, CreateApacheVHost(project, apachePort.Value), cancellationToken);
            _logger.LogInformation("Generated Apache vhost for {Project} at {Path}", project.Name, vhostPath);
        }
    }

    private async Task GenerateHostsPreviewAsync(ProjectDomainPlan domainPlan, CancellationToken cancellationToken)
    {
        var hostsPreviewPath = Path.Combine(_paths.ConfigRoot, "hosts.locora.generated");
        var content = new StringBuilder();
        content.AppendLine("# Locora generated hosts preview");
        content.AppendLine("# Copy these entries into the Windows hosts file only after reviewing them.");
        content.AppendLine("# Future privileged hosts automation should use this output as its safe preview.");
        if (domainPlan.Collisions.Count > 0)
        {
            content.AppendLine("#");
            content.AppendLine("# Skipped conflicting domains until each project has a unique hostname:");
            foreach (var collision in domainPlan.Collisions)
            {
                content.Append("# ");
                content.Append(collision.Host);
                content.Append(" -> ");
                content.AppendLine(string.Join(", ", collision.Projects.Select(project => project.Name)));
            }
        }

        foreach (var project in domainPlan.UniqueProjects)
        {
            content.Append("127.0.0.1 ");
            content.AppendLine(GetHost(project));
        }

        await File.WriteAllTextAsync(hostsPreviewPath, content.ToString(), cancellationToken);
        _logger.LogInformation("Generated hosts preview at {Path}", hostsPreviewPath);
    }

    private string CreateNginxVHost(DiscoveredProject project)
    {
        var documentRoot = ToNginxPath(project.DocumentRoot);
        var serverName = new Uri(project.Url).Host;
        var certificateMaterial = _localSslService.GetProjectCertificateMaterial(project);

        if (project.UsesHttps && certificateMaterial is not null)
        {
            return $$"""
            server {
                listen       80;
                server_name  {{serverName}};
                return       301 https://$host$request_uri;
            }

            server {
                listen       443 ssl;
                server_name  {{serverName}};
                root         "{{documentRoot}}";
                index        index.php index.html index.htm;
                ssl_certificate "{{ToNginxPath(certificateMaterial.CertificatePath)}}";
                ssl_certificate_key "{{ToNginxPath(certificateMaterial.KeyPath)}}";
                ssl_protocols TLSv1.2 TLSv1.3;
                ssl_session_cache shared:LOCORA_SSL:10m;
                ssl_session_timeout 10m;

                location / {
                    try_files $uri $uri/ /index.php?$query_string;
                }

                location ~ \.php$ {
                    return 501;
                }
            }
            """;
        }

        return $$"""
        server {
            listen       80;
            server_name  {{serverName}};
            root         "{{documentRoot}}";
            index        index.php index.html index.htm;

            location / {
                try_files $uri $uri/ /index.php?$query_string;
            }

            location ~ \.php$ {
                return 501;
            }
        }
        """;
    }

    private string CreateApacheVHost(DiscoveredProject project, int apachePort)
    {
        var documentRoot = ToApachePath(project.DocumentRoot);
        var serverName = GetHost(project);
        var certificateMaterial = _localSslService.GetProjectCertificateMaterial(project);
        var sslComment = project.UsesHttps && certificateMaterial is not null
            ? $"""
                # Locora SSL material is available for future Apache HTTPS wiring:
                # Certificate: {ToApachePath(certificateMaterial.CertificatePath)}
                # Private key: {ToApachePath(certificateMaterial.KeyPath)}
              """
            : "# Locora SSL material is not available for this project yet.";

        return $$"""
        <VirtualHost *:{{apachePort}}>
            ServerName {{serverName}}
            DocumentRoot "{{documentRoot}}"
            DirectoryIndex index.php index.html index.htm

            <Directory "{{documentRoot}}">
                Options FollowSymLinks
                AllowOverride All
                Require all granted
            </Directory>

            <IfModule rewrite_module>
                RewriteEngine On
            </IfModule>

            {{sslComment}}
            # PHP execution still needs a handler configuration in the Apache scaffold.
        </VirtualHost>
        """;
    }

    private static string ToNginxPath(string path) => path.Replace('\\', '/');

    private static string ToApachePath(string path) => path.Replace('\\', '/');

    private static string GetHost(DiscoveredProject project) => new Uri(project.Url).Host;

    private int? ResolveApachePort()
    {
        var definition = _managedServices.Services.FirstOrDefault(service =>
            service.Kind.Equals("apache", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("apache", StringComparison.OrdinalIgnoreCase));

        return definition?.Port ?? (definition is null ? null : 80);
    }

    private ProjectGenerationOptions GetCurrentOptions()
    {
        var settingsPath = _paths.ProjectsSettingsFile;
        if (!File.Exists(settingsPath))
        {
            return new ProjectGenerationOptions(
                DefaultProjectOptions.GenerateNginxVHosts,
                DefaultProjectOptions.GenerateApacheVHosts,
                DefaultProjectOptions.GenerateHostsPreview);
        }

        try
        {
            var content = File.ReadAllText(settingsPath);
            var document = JsonSerializer.Deserialize<ProjectSettingsDocument>(content, SerializerOptions);
            var options = document?.LocoraProjects;

            return new ProjectGenerationOptions(
                options?.GenerateNginxVHosts ?? DefaultProjectOptions.GenerateNginxVHosts,
                options?.GenerateApacheVHosts ?? DefaultProjectOptions.GenerateApacheVHosts,
                options?.GenerateHostsPreview ?? DefaultProjectOptions.GenerateHostsPreview);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load project generation settings from {SettingsPath}", settingsPath);
            return new ProjectGenerationOptions(
                DefaultProjectOptions.GenerateNginxVHosts,
                DefaultProjectOptions.GenerateApacheVHosts,
                DefaultProjectOptions.GenerateHostsPreview);
        }
    }

    private static ProjectDomainPlan CreateDomainPlan(IReadOnlyList<DiscoveredProject> projects)
    {
        var collisions = projects
            .GroupBy(GetHost, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new DomainCollision(group.Key, group.OrderBy(project => project.Name).ToList()))
            .OrderBy(collision => collision.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var collidingHosts = collisions
            .Select(collision => collision.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uniqueProjects = projects
            .Where(project => !collidingHosts.Contains(GetHost(project)))
            .ToList();

        return new ProjectDomainPlan(uniqueProjects, collisions);
    }

    private sealed record DomainCollision(string Host, IReadOnlyList<DiscoveredProject> Projects);

    private sealed record ProjectDomainPlan(
        IReadOnlyList<DiscoveredProject> UniqueProjects,
        IReadOnlyList<DomainCollision> Collisions);

    private sealed record ProjectGenerationOptions(
        bool GenerateNginxVHosts,
        bool GenerateApacheVHosts,
        bool GenerateHostsPreview);

    private sealed record ProjectSettingsDocument(ProjectDiscoveryOptions? LocoraProjects);
}
