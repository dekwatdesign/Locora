using Windows.ApplicationModel.DataTransfer;

namespace Locora.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
