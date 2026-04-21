using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class ApacheManagedService : ManagedServiceBase
{
    public ApacheManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<ApacheManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
    }

    public override async Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await base.GetStatusAsync(cancellationToken);
        var url = $"http://127.0.0.1:{status.Port ?? 8080}/";
        var note = string.IsNullOrWhiteSpace(status.Note)
            ? $"Apache console server at {url}"
            : $"{status.Note} {url}";

        return status with { Note = note };
    }
}
