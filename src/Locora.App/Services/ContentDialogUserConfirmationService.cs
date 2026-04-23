using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Locora.App.Services;

public sealed class ContentDialogUserConfirmationService : IUserConfirmationService
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);
    private readonly ILogger<ContentDialogUserConfirmationService> _logger;

    public ContentDialogUserConfirmationService(ILogger<ContentDialogUserConfirmationService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText = "Cancel",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryButtonText);
        ArgumentException.ThrowIfNullOrWhiteSpace(closeButtonText);

        var window = App.CurrentMainWindow ?? throw new InvalidOperationException("The main window is not available.");

        await _dialogLock.WaitAsync(cancellationToken);
        try
        {
            return await ShowOnWindowAsync(window, title, message, primaryButtonText, closeButtonText, cancellationToken);
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async Task<bool> ShowOnWindowAsync(
        MainWindow window,
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText,
        CancellationToken cancellationToken)
    {
        if (window.DispatcherQueue.HasThreadAccess)
        {
            return await ShowCoreAsync(window, title, message, primaryButtonText, closeButtonText);
        }

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completionSource.SetResult(await ShowCoreAsync(window, title, message, primaryButtonText, closeButtonText));
                }
                catch (Exception exception)
                {
                    completionSource.SetException(exception);
                }
            }))
        {
            _logger.LogWarning("Failed to enqueue confirmation dialog '{Title}'.", title);
            throw new InvalidOperationException("The confirmation dialog could not be shown.");
        }

        return await completionSource.Task.WaitAsync(cancellationToken);
    }

    private static async Task<bool> ShowCoreAsync(
        MainWindow window,
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText)
    {
        if (window.Content is not FrameworkElement rootElement || rootElement.XamlRoot is null)
        {
            throw new InvalidOperationException("The main window is not ready to host confirmation dialogs.");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = rootElement.XamlRoot,
            Title = title,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 480,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.WrapWholeWords
                    }
                }
            }
        };

        return await dialog.ShowAsync(ContentDialogPlacement.Popup) == ContentDialogResult.Primary;
    }
}
