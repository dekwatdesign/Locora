using System.Text;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ProjectConfigurationWriter
{
    private readonly IEnvironmentPaths _paths;
    private readonly ProjectDiscoveryOptions _options;
    private readonly LocalSslService _localSslService;
    private readonly ILogger<ProjectConfigurationWriter> _logger;

    public ProjectConfigurationWriter(
        IEnvironmentPaths paths,
        IOptions<ProjectDiscoveryOptions> options,
        LocalSslService localSslService,
        ILogger<ProjectConfigurationWriter> logger)
    {
        _paths = paths;
        _options = options.Value;
        _localSslService = localSslService;
        _logger = logger;
    }

    public async Task GenerateAsync(IReadOnlyList<DiscoveredProject> projects, CancellationToken cancellationToken = default)
    {
        if (_options.GenerateNginxVHosts)
        {
            await GenerateNginxVHostsAsync(projects, cancellationToken);
        }

        if (_options.GenerateHostsPreview)
        {
            await GenerateHostsPreviewAsync(projects, cancellationToken);
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

    private async Task GenerateHostsPreviewAsync(IReadOnlyList<DiscoveredProject> projects, CancellationToken cancellationToken)
    {
        var hostsPreviewPath = Path.Combine(_paths.ConfigRoot, "hosts.locora.generated");
        var content = new StringBuilder();
        content.AppendLine("# Locora generated hosts preview");
        content.AppendLine("# Copy these entries into the Windows hosts file only after reviewing them.");
        content.AppendLine("# Future privileged hosts automation should use this output as its safe preview.");

        foreach (var project in projects)
        {
            content.Append("127.0.0.1 ");
            content.Append(project.Slug);
            content.Append('.');
            content.AppendLine(_options.DomainSuffix);
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

    private static string ToNginxPath(string path) => path.Replace('\\', '/');
}
