using System.Text.Json;
using Locora.Application.Abstractions;

namespace Locora.App.Services;

public sealed class ProjectPinStore : IProjectPinStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly IEnvironmentPaths _paths;

    public ProjectPinStore(IEnvironmentPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyCollection<string>> LoadPinnedProjectKeysAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ProjectPinsSettingsFile))
        {
            return Array.Empty<string>();
        }

        await using var stream = File.OpenRead(_paths.ProjectPinsSettingsFile);
        var document = await JsonSerializer.DeserializeAsync<ProjectPinsDocument>(stream, SerializerOptions, cancellationToken);
        return NormalizeProjectKeys(document?.PinnedProjectKeys);
    }

    public async Task SavePinnedProjectKeysAsync(IEnumerable<string> projectKeys, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_paths.ProjectPinsSettingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new ProjectPinsDocument(NormalizeProjectKeys(projectKeys).ToArray());
        await using var stream = File.Create(_paths.ProjectPinsSettingsFile);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
    }

    private static IReadOnlyCollection<string> NormalizeProjectKeys(IEnumerable<string>? projectKeys)
    {
        return (projectKeys ?? Array.Empty<string>())
            .Where(projectKey => !string.IsNullOrWhiteSpace(projectKey))
            .Select(projectKey => projectKey.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(projectKey => projectKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record ProjectPinsDocument(string[] PinnedProjectKeys);
}
