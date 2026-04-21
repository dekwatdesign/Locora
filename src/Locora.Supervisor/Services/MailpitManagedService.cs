using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class MailpitManagedService : ManagedServiceBase
{
    private const string DefaultUiBind = "127.0.0.1:8025";

    public MailpitManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<MailpitManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
    }

    public override async Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await base.GetStatusAsync(cancellationToken);
        var smtpEndpoint = $"127.0.0.1:{status.Port ?? 1025}";
        var uiUrl = BuildUiUrl();
        var summary = $"SMTP {smtpEndpoint} / UI {uiUrl}";
        var note = string.IsNullOrWhiteSpace(status.Note)
            ? summary
            : $"{status.Note} {summary}";

        return status with { Note = note };
    }

    private string BuildUiUrl()
    {
        var bindAddress = TryGetArgumentValue("--listen") ?? DefaultUiBind;
        var normalizedAddress = NormalizeLoopback(bindAddress);
        return normalizedAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalizedAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalizedAddress.TrimEnd('/') + "/"
            : $"http://{normalizedAddress.TrimEnd('/')}/";
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

    private static string NormalizeLoopback(string bindAddress)
    {
        if (bindAddress.StartsWith("0.0.0.0:", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1" + bindAddress["0.0.0.0".Length..];
        }

        if (bindAddress.StartsWith(":", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1" + bindAddress;
        }

        return bindAddress;
    }
}
