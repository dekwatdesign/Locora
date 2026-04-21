using Microsoft.UI.Xaml;

namespace Locora.App.Services;

public interface ITrayService
{
    void Initialize(Window window);

    void ShowStatus(string message);
}
