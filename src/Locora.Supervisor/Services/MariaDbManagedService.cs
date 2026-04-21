using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class MariaDbManagedService : ManagedServiceBase
{
    public MariaDbManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<MariaDbManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
    }
}
