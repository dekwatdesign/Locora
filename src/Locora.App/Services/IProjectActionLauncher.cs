namespace Locora.App.Services;

public interface IProjectActionLauncher
{
    void OpenUrl(string url);

    void OpenFile(string filePath);

    void OpenFolder(string folderPath);

    void OpenTerminal(string folderPath);

    void OpenEditor(string folderPath);
}
