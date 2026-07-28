using Locora.App.Contracts;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Windows.Globalization;

namespace Locora.App.Services;

public sealed class LocoraLanguageService
{
    private readonly AppSettings _settings;

    public LocoraLanguageService(IOptions<AppSettings> settings)
    {
        _settings = settings.Value;
    }

    public void ApplyConfiguredLanguage()
    {
        ApplicationLanguages.PrimaryLanguageOverride =
            LocoraLanguagePreference.ResolveOverride(_settings.Experience.Language);
    }
}
