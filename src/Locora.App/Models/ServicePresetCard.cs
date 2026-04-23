using CommunityToolkit.Mvvm.Input;

namespace Locora.App.Models;

public sealed record ServicePresetCard(
    string Key,
    string DisplayName,
    string Description,
    string State,
    string ServicesLabel,
    string TagsLabel,
    bool IsValid,
    string Summary,
    string Details,
    IAsyncRelayCommand<ServicePresetCard> StartPresetCommand,
    IAsyncRelayCommand<ServicePresetCard> StopPresetCommand)
{
    public bool CanRun => IsValid;

    public string StartAutomationName => $"Start service preset {DisplayName}";

    public string StopAutomationName => $"Stop service preset {DisplayName}";
}
