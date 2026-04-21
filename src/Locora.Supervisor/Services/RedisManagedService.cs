using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class RedisManagedService : ManagedServiceBase
{
    public RedisManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<RedisManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
    }
}
