using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Locora.Infrastructure.Tests;

public sealed class EnvironmentPathsTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");

    [Fact]
    public void UsesLocoraRootOverrideForDerivedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "locora infrastructure tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LOCORA_ROOT", root);

        var paths = new EnvironmentPaths(Options.Create(new AppSettings()));

        Assert.Equal(Path.GetFullPath(root), paths.AppRoot);
        Assert.Equal(Path.Combine(paths.UserRoot, "distribution"), paths.PortableDistributionRoot);
        Assert.Equal(Path.Combine(paths.PortableDistributionRoot, "artifacts"), paths.PortableDistributionArtifactsRoot);
        Assert.Equal(Path.Combine(paths.PortableDistributionRoot, "build-portable-distribution.ps1"), paths.PortableDistributionScriptFile);
        Assert.Equal(Path.Combine(paths.UserRoot, "updates", "release-manifest.example.json"), paths.AppUpdateManifestExampleFile);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _previousRoot);
    }
}
