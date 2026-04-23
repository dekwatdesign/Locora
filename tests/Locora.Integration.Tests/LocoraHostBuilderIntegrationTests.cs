using System.Text.Json;
using Locora.Application.Abstractions;
using Locora.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Locora.Integration.Tests;

public sealed class LocoraHostBuilderIntegrationTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "locora host integration", Guid.NewGuid().ToString("N"));

    public LocoraHostBuilderIntegrationTests()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _root);
    }

    [Fact]
    public async Task StartAsync_BootstrapsPortableLayoutAndDefaultConfig()
    {
        using var host = LocoraHostBuilder.BuildHost("integration-test", (_, _) => { });

        await host.StartAsync();
        await host.StopAsync();

        var paths = host.Services.GetRequiredService<IEnvironmentPaths>();
        using var appSettings = JsonDocument.Parse(await File.ReadAllTextAsync(paths.AppSettingsFile));

        Assert.Equal(Path.GetFullPath(_root), paths.AppRoot);
        Assert.True(Directory.Exists(paths.AppUpdateDownloadRoot));
        Assert.True(Directory.Exists(paths.PortableDistributionArtifactsRoot));
        Assert.True(File.Exists(Path.Combine(paths.ProjectRoot, "welcome", "index.html")));
        Assert.Equal("stable", appSettings.RootElement.GetProperty("Locora").GetProperty("Updates").GetProperty("Channel").GetString());
        Assert.Equal("win-x64", appSettings.RootElement.GetProperty("Locora").GetProperty("Distribution").GetProperty("RuntimeIdentifier").GetString());
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
