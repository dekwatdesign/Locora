using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class MemcachedManagedService : ManagedServiceBase
{
    private const string DefaultBindHost = "127.0.0.1";
    private const int DefaultMemoryLimitMb = 64;

    public MemcachedManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<MemcachedManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
    }

    public override async Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await base.GetStatusAsync(cancellationToken);
        var bindHost = NormalizeLoopbackHost(TryGetArgumentValue("-l") ?? DefaultBindHost);
        var memoryLimitMb = TryParseInt(TryGetArgumentValue("-m"), DefaultMemoryLimitMb);
        var endpointSummary = $"TCP {bindHost}:{status.Port ?? 11211} / cache {memoryLimitMb} MB";
        var note = string.IsNullOrWhiteSpace(status.Note)
            ? endpointSummary
            : $"{status.Note} {endpointSummary}";

        return status with { Note = note };
    }

    private string? TryGetArgumentValue(string flag)
    {
        for (var index = 0; index < Definition.Arguments.Count - 1; index++)
        {
            if (Definition.Arguments[index].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return ExpandToken(Definition.Arguments[index + 1]);
            }
        }

        return null;
    }

    private static int TryParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static string NormalizeLoopbackHost(string host)
    {
        return host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)
            ? DefaultBindHost
            : host;
    }
}
