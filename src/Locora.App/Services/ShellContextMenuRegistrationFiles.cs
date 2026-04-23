namespace Locora.App.Services;

public sealed record ShellContextMenuRegistrationFiles(
    string RootPath,
    string InstallFilePath,
    string UninstallFilePath,
    string ReadmeFilePath,
    string AppExecutablePath);
