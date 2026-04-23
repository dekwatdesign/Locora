using Locora.App.Contracts;
using Locora.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Locora.E2E.Tests;

public sealed class SupervisorPipeE2ETests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
    private readonly string? _previousPipeName = Environment.GetEnvironmentVariable("LOCORA_Locora__Supervisor__PipeName");
    private readonly string? _previousConnectTimeout = Environment.GetEnvironmentVariable("LOCORA_Locora__Supervisor__ConnectTimeoutMs");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "locora e2e", Guid.NewGuid().ToString("N"));
    private readonly string _pipeName = $"locora-e2e-{Guid.NewGuid():N}";

    public SupervisorPipeE2ETests()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _root);
        Environment.SetEnvironmentVariable("LOCORA_Locora__Supervisor__PipeName", _pipeName);
        Environment.SetEnvironmentVariable("LOCORA_Locora__Supervisor__ConnectTimeoutMs", "5000");
    }

    [Fact]
    public async Task WorkbenchCanReachSupervisorThroughNamedPipeAndReadDiscoveredProjects()
    {
        using var host = E2ESupervisorHostFactory.Build();
        await host.StartAsync();

        try
        {
            var client = host.Services.GetRequiredService<ISupervisorClient>();
            var dto = await WaitForSupervisorSnapshotAsync(client);

            var workbench = host.Services.GetRequiredService<IWorkbenchService>();
            var snapshot = await workbench.GetSnapshotAsync();
            var savedSnapshot = await workbench.SaveActiveEnvironmentAsync("E2E Smoke");

            Assert.Equal(Path.GetFullPath(_root), dto.EnvironmentRoot);
            Assert.True(snapshot.IsSupervisorReachable);
            Assert.Equal(Path.GetFullPath(_root), snapshot.EnvironmentRoot);
            Assert.Contains(snapshot.Services, service => service.Key == "nginx");
            Assert.Contains(snapshot.Projects, project => project.Name == "welcome" && project.Url == "http://welcome.locora.test");
            Assert.Contains(savedSnapshot.StackProfiles, profile => profile.DisplayName == "E2E Smoke" && profile.IsActive);

            Assert.True(File.Exists(Path.Combine(_root, "usr", "config", "nginx", "nginx.conf")));
            Assert.True(File.Exists(Path.Combine(_root, "usr", "config", "nginx", "vhosts", "welcome.conf")));
            Assert.True(File.Exists(Path.Combine(_root, "usr", "config", "hosts.locora.generated")));
            Assert.True(File.Exists(Path.Combine(_root, "usr", "config", "profiles.json")));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<EnvironmentSnapshotDto> WaitForSupervisorSnapshotAsync(ISupervisorClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await client.GetEnvironmentSnapshotAsync();
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException)
            {
                lastException = exception;
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("Timed out waiting for the E2E supervisor pipe server.", lastException);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _previousRoot);
        Environment.SetEnvironmentVariable("LOCORA_Locora__Supervisor__PipeName", _previousPipeName);
        Environment.SetEnvironmentVariable("LOCORA_Locora__Supervisor__ConnectTimeoutMs", _previousConnectTimeout);

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
