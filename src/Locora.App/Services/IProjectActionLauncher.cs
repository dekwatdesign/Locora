using Locora.App.Models;

namespace Locora.App.Services;

public interface IProjectActionLauncher
{
    void OpenUrl(string url);

    void OpenFile(string filePath);

    void OpenFolder(string folderPath);

    void RevealInExplorer(string path);

    void OpenTerminal(ProjectTerminalLaunchProfile launchProfile);

    void OpenDatabaseAdminTool(string serviceKey, string workingDirectory);

    void OpenEditor(string folderPath);
}
