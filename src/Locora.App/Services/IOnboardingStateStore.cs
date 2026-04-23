namespace Locora.App.Services;

public interface IOnboardingStateStore
{
    Task<bool> LoadFirstRunDismissedAsync(CancellationToken cancellationToken = default);

    Task SaveFirstRunDismissedAsync(bool isDismissed, CancellationToken cancellationToken = default);
}
