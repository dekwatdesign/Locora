using Locora.App.Contracts;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Locora.App.Services;

public sealed class LocoraStringResourceService : ILocoraStringResourceService
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CommandPalette.Placeholder"] = "Search pages, commands, projects (Ctrl+K)",
        ["Nav.Home"] = "Home",
        ["Nav.Workbench"] = "Workbench",
        ["Nav.Health"] = "Health",
        ["Nav.Terminal"] = "Terminal",
        ["Nav.Advanced"] = "Advanced"
    };

    private static readonly IReadOnlyDictionary<string, string> Thai = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CommandPalette.Placeholder"] = "ค้นหาหน้า คำสั่ง โปรเจกต์ (Ctrl+K)",
        ["Nav.Home"] = "หน้าแรก",
        ["Nav.Workbench"] = "พื้นที่ทำงาน",
        ["Nav.Health"] = "สถานะระบบ",
        ["Nav.Terminal"] = "เทอร์มินัล",
        ["Nav.Advanced"] = "ขั้นสูง"
    };

    public LocoraStringResourceService(IOptions<AppSettings> settings)
    {
        Language = LocoraLanguagePreference.Normalize(settings.Value.Experience.Language);
    }

    public string Language { get; }

    public string Get(string key)
    {
        var table = Language.Equals("th-TH", StringComparison.OrdinalIgnoreCase)
            ? Thai
            : English;

        if (table.TryGetValue(key, out var value) || English.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }
}
