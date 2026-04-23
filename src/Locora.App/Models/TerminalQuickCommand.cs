using System.Collections.Generic;

namespace Locora.App.Models;

public sealed record TerminalQuickCommand(
    string Key,
    string DisplayName,
    string Alias,
    string Description,
    string CommandText,
    string Scope,
    IReadOnlyList<string> Tags)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Alias)
        ? DisplayName
        : $"{DisplayName} ({Alias})";

    public string Summary => string.IsNullOrWhiteSpace(Description)
        ? CommandText
        : Description;

    public string ScopeLabel => Scope.ToLowerInvariant() switch
    {
        "project" => "Project tab",
        "root" => "Locora root tab",
        _ => "Any terminal tab"
    };

    public string SearchText => $"{Key} {DisplayName} {Alias} {Description} {CommandText} {Scope} {string.Join(' ', Tags)}";

    public string RunAutomationName => $"Run terminal command {DisplayName}";
}
