using System.Text.Json;
using Locora.Application.Abstractions;

namespace Locora.App.Services;

public sealed class OnboardingStateStore : IOnboardingStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly IEnvironmentPaths _paths;

    public OnboardingStateStore(IEnvironmentPaths paths)
    {
        _paths = paths;
    }

    public async Task<bool> LoadFirstRunDismissedAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.OnboardingSettingsFile))
        {
            return false;
        }

        await using var stream = File.OpenRead(_paths.OnboardingSettingsFile);
        var document = await JsonSerializer.DeserializeAsync<OnboardingDocument>(stream, SerializerOptions, cancellationToken);
        return document?.FirstRunDismissed ?? false;
    }

    public async Task SaveFirstRunDismissedAsync(bool isDismissed, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_paths.OnboardingSettingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_paths.OnboardingSettingsFile);
        await JsonSerializer.SerializeAsync(stream, new OnboardingDocument(isDismissed), SerializerOptions, cancellationToken);
    }

    private sealed record OnboardingDocument(bool FirstRunDismissed);
}
