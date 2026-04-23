using CommunityToolkit.Mvvm.Input;

namespace Locora.App.Models;

public sealed record CustomToolMenuItem(
    string Key,
    string DisplayName,
    string Description,
    string Action,
    string Target,
    string Scope,
    IReadOnlyList<string> Tags,
    bool IsEnabled,
    IRelayCommand<CustomToolMenuItem> RunCommand)
{
    public bool CanRun => IsEnabled && !string.IsNullOrWhiteSpace(Target);

    public string ActionLabel => NormalizeAction(Action) switch
    {
        "url" => "URL",
        "folder" => "Folder",
        "file" => "File",
        "editor" => "Editor",
        "terminal" => "Terminal",
        _ => "Tool"
    };

    public string ScopeLabel => NormalizeScope(Scope) switch
    {
        "project" => "Project",
        "root" => "Locora root",
        _ => "Any context"
    };

    public string Summary => string.IsNullOrWhiteSpace(Description)
        ? Target
        : Description;

    public string TargetLabel => Target;

    public string TagsLabel => Tags.Count == 0 ? "Tags: none" : $"Tags: {string.Join(", ", Tags)}";

    public string RunAutomationName => $"Run custom tool {DisplayName}";

    public string SearchText => $"{Key} {DisplayName} {Description} {Action} {Target} {Scope} {string.Join(' ', Tags)}";

    private static string NormalizeAction(string action) => string.IsNullOrWhiteSpace(action)
        ? "terminal"
        : action.Trim().ToLowerInvariant();

    private static string NormalizeScope(string scope) => string.IsNullOrWhiteSpace(scope)
        ? "any"
        : scope.Trim().ToLowerInvariant();
}
