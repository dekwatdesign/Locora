using System.Collections;
using System.Diagnostics;
using Locora.App.Models;

namespace Locora.App.Services;

internal static class TerminalLaunchEnvironment
{
    public static void Apply(ProcessStartInfo startInfo, ProjectTerminalLaunchProfile launchProfile)
    {
        var environment = Build(launchProfile);

        startInfo.Environment.Clear();
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }

    public static IReadOnlyDictionary<string, string> Build(ProjectTerminalLaunchProfile launchProfile)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => entry.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in launchProfile.EnvironmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        if (launchProfile.PathEntries.Count == 0)
        {
            return environment;
        }

        var inheritedPath = environment.TryGetValue("PATH", out var pathValue)
            ? pathValue
            : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var combinedPath = launchProfile.PathEntries
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Concat(inheritedPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        environment["PATH"] = string.Join(Path.PathSeparator, combinedPath);
        return environment;
    }
}
