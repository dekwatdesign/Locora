using System.Windows.Input;

namespace Locora.App.Models;

public sealed record ProjectCard(
    string Name,
    string Url,
    string Runtime,
    string Folder,
    ICommand OpenUrlCommand,
    ICommand OpenFolderCommand,
    ICommand OpenTerminalCommand,
    ICommand OpenEditorCommand);
