namespace Locora.App.Services;

public interface IUserConfirmationService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText = "Cancel",
        CancellationToken cancellationToken = default);
}
