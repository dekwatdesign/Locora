using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Locora.App.Services;

public sealed class NoOpTrayService : ITrayService
{
    private readonly ILogger<NoOpTrayService> _logger;

    public NoOpTrayService(ILogger<NoOpTrayService> logger)
    {
        _logger = logger;
    }

    public void Initialize(Window window)
    {
        _logger.LogInformation("Tray integration is scaffolded but not yet implemented.");
    }

    public void ShowStatus(string message)
    {
        _logger.LogInformation("Tray status: {Message}", message);
    }
}
