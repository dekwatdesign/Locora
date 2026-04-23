using System.Windows.Input;

namespace Locora.App.Models;

public sealed record StackProfileCard(
    string Key,
    string DisplayName,
    string Description,
    string State,
    string ServicesLabel,
    string PackagesLabel,
    string TagsLabel,
    bool IsActive,
    bool IsValid,
    string Summary,
    string Details,
    ICommand SelectProfileCommand)
{
    public string SelectButtonLabel => IsActive ? "Loaded" : "Load";

    public bool CanSelect => !IsActive && IsValid;

    public string SelectAutomationName => IsActive
        ? $"{DisplayName} stack profile is loaded"
        : $"Load {DisplayName} stack profile";
}
