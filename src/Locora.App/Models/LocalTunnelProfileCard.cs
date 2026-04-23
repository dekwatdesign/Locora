using CommunityToolkit.Mvvm.Input;

namespace Locora.App.Models;

public sealed record LocalTunnelProfileCard(
    string Key,
    string DisplayName,
    string Provider,
    string Description,
    string CommandText,
    string Scope,
    string ProjectName,
    string LocalUrl,
    int? Port,
    IReadOnlyList<string> Tags,
    bool IsEnabled,
    IRelayCommand<LocalTunnelProfileCard> StartCommand)
{
    public bool CanStart => IsEnabled && !string.IsNullOrWhiteSpace(CommandText);

    public string ProviderLabel => string.IsNullOrWhiteSpace(Provider)
        ? "Provider: custom"
        : $"Provider: {Provider}";

    public string ScopeLabel => NormalizeScope(Scope) switch
    {
        "project" => string.IsNullOrWhiteSpace(ProjectName) ? "Project tunnel" : $"Project tunnel: {ProjectName}",
        "root" => "Root tunnel",
        _ => "Any terminal"
    };

    public string TargetLabel => string.IsNullOrWhiteSpace(LocalUrl)
        ? Port is null ? "Target: selected project URL" : $"Target: port {Port}"
        : $"Target: {LocalUrl}";

    public string CommandLabel => $"Command: {CommandText}";

    public string Summary => string.IsNullOrWhiteSpace(Description)
        ? TargetLabel
        : Description;

    public string TagsLabel => Tags.Count == 0 ? "Tags: none" : $"Tags: {string.Join(", ", Tags)}";

    public string StartAutomationName => $"Start tunnel {DisplayName}";

    public string SearchText => $"{Key} {DisplayName} {Provider} {Description} {CommandText} {Scope} {ProjectName} {LocalUrl} {Port} {string.Join(' ', Tags)}";

    private static string NormalizeScope(string scope) => string.IsNullOrWhiteSpace(scope)
        ? "project"
        : scope.Trim().ToLowerInvariant();
}
