using Locora.Domain.Entities;
using Locora.Domain.Enums;
using Xunit;

namespace Locora.Domain.Tests;

public sealed class EnvironmentSnapshotTests
{
    [Fact]
    public void ServiceDescriptorPreservesConfiguredPortAndState()
    {
        var service = new ServiceDescriptor(
            Key: "nginx",
            DisplayName: "Nginx",
            Version: "1.27",
            Port: 80,
            State: ServiceState.Running,
            AutoStart: true,
            Note: "ready");

        Assert.Equal("nginx", service.Key);
        Assert.Equal(80, service.Port);
        Assert.Equal(ServiceState.Running, service.State);
        Assert.True(service.AutoStart);
    }
}
