using Locora.Supervisor.Services;
using Xunit;

namespace Locora.Supervisor.Tests;

public sealed class PortDiagnosticsServiceTests
{
    [Fact]
    public void GetDiagnostics_WhenNoConfiguredPorts_ReturnsEmptyList()
    {
        var diagnostics = new PortDiagnosticsService().GetDiagnostics(
        [
            CreateStatus("worker", "Worker", null)
        ]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetDiagnostics_WhenTwoServicesSharePort_ReturnsBlockedDiagnostics()
    {
        var diagnostics = new PortDiagnosticsService().GetDiagnostics(
        [
            CreateStatus("nginx", "Nginx", 8080),
            CreateStatus("apache", "Apache", 8080)
        ]);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("Blocked", diagnostic.State);
            Assert.Equal(8080, diagnostic.Port);
            Assert.Contains("multiple Locora services", diagnostic.Summary, StringComparison.Ordinal);
        });
    }

    private static ManagedServiceStatus CreateStatus(string key, string displayName, int? port)
    {
        return new ManagedServiceStatus(
            Key: key,
            DisplayName: displayName,
            Version: "1.0",
            Port: port,
            State: "Stopped",
            AutoStart: false,
            Note: null,
            ExecutableExists: true,
            PortResponsive: false,
            ProcessId: null,
            ExecutablePath: $"{key}.exe");
    }
}
