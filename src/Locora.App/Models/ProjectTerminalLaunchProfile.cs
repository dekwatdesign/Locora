using System.Collections.Generic;

namespace Locora.App.Models;

public sealed record ProjectTerminalLaunchProfile(
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<string> PathEntries);
