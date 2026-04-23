using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Services;
using Locora.Supervisor.Configuration;
using Locora.Supervisor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Locora.Integration.Tests;

public sealed class ProjectConfigurationWriterIntegrationTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "locora project config integration", Guid.NewGuid().ToString("N"));

    public ProjectConfigurationWriterIntegrationTests()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _root);
    }

    [Fact]
    public async Task GenerateAsync_WritesNginxVhostAndHostsPreview()
    {
        var paths = new EnvironmentPaths(Options.Create(new AppSettings()));
        Directory.CreateDirectory(paths.ConfigRoot);
        Directory.CreateDirectory(paths.ProjectRoot);

        var projectRoot = Path.Combine(paths.ProjectRoot, "acme");
        Directory.CreateDirectory(projectRoot);

        var writer = new ProjectConfigurationWriter(
            paths,
            Options.Create(new ManagedServicesOptions()),
            new LocalSslService(paths, NullLogger<LocalSslService>.Instance),
            NullLogger<ProjectConfigurationWriter>.Instance);

        await writer.GenerateAsync(
        [
            new DiscoveredProject(
                Name: "Acme",
                Slug: "acme",
                Path: projectRoot,
                DocumentRoot: projectRoot,
                Url: "http://acme.locora.test",
                Runtime: "PHP",
                Framework: "Generic",
                Description: "Integration fixture",
                Tags: ["fixture"],
                UsesHttps: false,
                OverrideSummary: "Overrides: none")
        ]);

        var vhostPath = Path.Combine(paths.ConfigRoot, "nginx", "vhosts", "acme.conf");
        var hostsPreviewPath = Path.Combine(paths.ConfigRoot, "hosts.locora.generated");

        var vhost = await File.ReadAllTextAsync(vhostPath);
        var hostsPreview = await File.ReadAllTextAsync(hostsPreviewPath);

        Assert.Contains("server_name  acme.locora.test", vhost, StringComparison.Ordinal);
        Assert.Contains(projectRoot.Replace('\\', '/'), vhost, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1 acme.locora.test", hostsPreview, StringComparison.Ordinal);
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
