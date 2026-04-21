using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class ManagedServiceFactory
{
    private readonly IEnvironmentPaths _paths;
    private readonly PortProbe _portProbe;
    private readonly ILoggerFactory _loggerFactory;

    public ManagedServiceFactory(
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _portProbe = portProbe;
        _loggerFactory = loggerFactory;
    }

    public IManagedService Create(ManagedServiceDefinition definition)
    {
        return definition.Kind.ToLowerInvariant() switch
        {
            "nginx" => new NginxManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<NginxManagedService>()),
            "apache" => new ApacheManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<ApacheManagedService>()),
            "mariadb" or "mysql" => new MariaDbManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<MariaDbManagedService>()),
            "postgresql" or "postgres" => new PostgreSqlManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<PostgreSqlManagedService>()),
            "redis" => new RedisManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<RedisManagedService>()),
            "memcached" => new MemcachedManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<MemcachedManagedService>()),
            "mailpit" => new MailpitManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<MailpitManagedService>()),
            _ => new GenericManagedService(definition, _paths, _portProbe, _loggerFactory.CreateLogger<GenericManagedService>())
        };
    }
}
