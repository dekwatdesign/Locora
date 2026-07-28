using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Locora.App.Converters;

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString() ?? string.Empty;
        var key = ResolveBrushKey(text);

        return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush resolved
            ? resolved
            : Microsoft.UI.Xaml.Application.Current.Resources["LocoraStatusNeutralBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static string ResolveBrushKey(string text)
    {
        if (ContainsAny(text, "running", "ready", "valid", "trusted", "clear", "loaded", "active", "success"))
        {
            return "LocoraStatusSuccessBrush";
        }

        if (ContainsAny(text, "warning", "pending", "waiting", "almost", "unverified", "expiring"))
        {
            return "LocoraStatusWarningBrush";
        }

        if (ContainsAny(text, "error", "invalid", "failed", "missing", "mismatch", "expired", "blocked", "offline", "not connected"))
        {
            return "LocoraStatusDangerBrush";
        }

        if (ContainsAny(text, "stopped", "disabled", "unknown", "unavailable"))
        {
            return "LocoraStatusNeutralBrush";
        }

        return "LocoraStatusAccentBrush";
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
