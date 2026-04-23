namespace Locora.App.Services;

public interface IAppUpdateService
{
    AppUpdateStatus GetCurrentStatus();

    Task<AppUpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
