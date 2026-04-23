using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Services;
using Locora.Supervisor.Configuration;
using Locora.Supervisor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Locora.Snapshot.Tests;

public sealed class GeneratedConfigSnapshotTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "locora snapshot tests", Guid.NewGuid().ToString("N"));

    public GeneratedConfigSnapshotTests()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _root);
    }

    [Fact]
    public async Task GeneratedNginxAndProjectConfigMatchApprovedSnapshots()
    {
        var paths = new EnvironmentPaths(Options.Create(new AppSettings()));
        var managedServices = new ManagedServicesOptions
        {
            Services =
            [
                new ManagedServiceDefinition
                {
                    Key = "nginx",
                    DisplayName = "Nginx",
                    Kind = "nginx",
                    Version = "1.27.x",
                    RelativeExecutablePath = "bin/nginx/current/nginx.exe",
                    RelativeWorkingDirectory = "bin/nginx/current",
                    Port = 80
                }
            ]
        };

        var serviceWriter = new ServiceConfigurationWriter(
            paths,
            Options.Create(managedServices),
            NullLogger<ServiceConfigurationWriter>.Instance);
        var projectWriter = new ProjectConfigurationWriter(
            paths,
            Options.Create(managedServices),
            new LocalSslService(paths, NullLogger<LocalSslService>.Instance),
            NullLogger<ProjectConfigurationWriter>.Instance);

        var documentRoot = Path.Combine(paths.ProjectRoot, "acme", "public");
        Directory.CreateDirectory(documentRoot);

        await serviceWriter.GenerateAllAsync();
        await projectWriter.GenerateAsync(
        [
            new DiscoveredProject(
                Name: "Acme",
                Slug: "acme",
                Path: Path.Combine(paths.ProjectRoot, "acme"),
                DocumentRoot: documentRoot,
                Url: "http://acme.locora.test",
                Runtime: "PHP",
                Framework: "Generic",
                Description: "Snapshot fixture",
                Tags: ["snapshot"],
                UsesHttps: false,
                OverrideSummary: "Overrides: none")
        ]);

        await AssertSnapshotAsync(
            "nginx.conf",
            Path.Combine(paths.ConfigRoot, "nginx", "nginx.conf"),
            """
            worker_processes  1;

            error_log  "<root>/usr/logs/nginx.stderr.log";
            pid        "<root>/temp/nginx/nginx.pid";

            events {
                worker_connections  1024;
            }

            http {

                default_type  application/octet-stream;

                access_log    "<root>/usr/logs/nginx.stdout.log";
                sendfile      on;
                keepalive_timeout  65;
                client_body_temp_path "<root>/temp/nginx/client_temp";
                proxy_temp_path       "<root>/temp/nginx/proxy_temp";
                fastcgi_temp_path     "<root>/temp/nginx/fastcgi_temp";
                uwsgi_temp_path       "<root>/temp/nginx/uwsgi_temp";
                scgi_temp_path        "<root>/temp/nginx/scgi_temp";

                server {
                    listen       80;
                    server_name  localhost *.locora.test;
                    root         "<root>/www";
                    index        index.php index.html index.htm;

                    location / {
                        try_files $uri $uri/ /index.php?$query_string;
                    }

                    location ~ \.php$ {
                        return 501;
                    }
                }

                include "<root>/usr/config/nginx/vhosts/*.conf";
            }
            """);

        await AssertSnapshotAsync(
            "acme.nginx-vhost.conf",
            Path.Combine(paths.ConfigRoot, "nginx", "vhosts", "acme.conf"),
            """
            server {
                listen       80;
                server_name  acme.locora.test;
                root         "<root>/www/acme/public";
                index        index.php index.html index.htm;

                location / {
                    try_files $uri $uri/ /index.php?$query_string;
                }

                location ~ \.php$ {
                    return 501;
                }
            }
            """);

        await AssertSnapshotAsync(
            "hosts.locora.generated",
            Path.Combine(paths.ConfigRoot, "hosts.locora.generated"),
            """
            # Locora generated hosts preview
            # Copy these entries into the Windows hosts file only after reviewing them.
            # Future privileged hosts automation should use this output as its safe preview.
            127.0.0.1 acme.locora.test
            """);
    }

    private async Task AssertSnapshotAsync(string name, string path, string expected)
    {
        var actual = NormalizeSnapshot(await File.ReadAllTextAsync(path));
        Assert.Equal(NormalizeSnapshot(expected), actual);
    }

    private string NormalizeSnapshot(string value)
    {
        var normalizedRoot = Path.GetFullPath(_root).Replace('\\', '/');
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\\', '/')
            .Replace(normalizedRoot, "<root>", StringComparison.OrdinalIgnoreCase);

        var lines = normalized.Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();

        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _previousRoot);

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
