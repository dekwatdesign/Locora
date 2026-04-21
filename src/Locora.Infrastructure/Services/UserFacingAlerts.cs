using System.Runtime.InteropServices;

namespace Locora.Infrastructure.Services;

public static class UserFacingAlerts
{
    private const uint OkWarningTopmost = 0x00000000 | 0x00000030 | 0x00040000;

    public static void ShowDuplicateInstance(string processDisplayName, string rootPath, bool timedOut)
    {
        var message = timedOut
            ? $"{processDisplayName} could not start because another instance for this Locora root is still active or shutting down.{Environment.NewLine}{Environment.NewLine}Root: {rootPath}{Environment.NewLine}{Environment.NewLine}Use the existing instance, or wait a few seconds and try again."
            : $"{processDisplayName} is already running for this Locora root.{Environment.NewLine}{Environment.NewLine}Root: {rootPath}{Environment.NewLine}{Environment.NewLine}Use the existing instance instead of opening another one.";

        Show(processDisplayName, message);
    }

    public static void Show(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            MessageBoxW(nint.Zero, message, title, OkWarningTopmost);
            return;
        }

        Console.Error.WriteLine($"{title}: {message}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
