using System.Windows.Input;

namespace Locora.App.Models;

public sealed record ProjectCard(
    string Key,
    string Name,
    string Url,
    string Runtime,
    string Folder,
    ProjectTerminalLaunchProfile TerminalLaunchProfile,
    string Description,
    string TagsLabel,
    bool IsPinned,
    string PinButtonLabel,
    ICommand TogglePinCommand,
    ICommand OpenUrlCommand,
    ICommand OpenFolderCommand,
    ICommand RevealInExplorerCommand,
    ICommand CopyFolderPathCommand,
    ICommand OpenTerminalCommand,
    ICommand OpenEditorCommand)
{
    public string PinAutomationName => $"{PinButtonLabel} project {Name}";

    public string OpenUrlAutomationName => $"Open site for {Name}";

    public string OpenFolderAutomationName => $"Open folder for {Name}";

    public string RevealInExplorerAutomationName => $"Reveal {Name} in File Explorer";

    public string CopyFolderPathAutomationName => $"Copy folder path for {Name}";

    public string OpenTerminalAutomationName => $"Open terminal for {Name}";

    public string OpenEditorAutomationName => $"Open editor for {Name}";
}
