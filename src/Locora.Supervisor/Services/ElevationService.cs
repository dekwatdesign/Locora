using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class ElevationService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ElevationService> _logger;

    public ElevationService(IHostApplicationLifetime lifetime, ILogger<ElevationService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task RestartElevatedAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Elevated supervisor restart is only supported on Windows.");
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not resolve the current supervisor executable path.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
        _logger.LogInformation("Requested elevated supervisor restart for {Path}", executablePath);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), CancellationToken.None);
            _lifetime.StopApplication();
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
