using System.Diagnostics;
using System.Text;
using Locora.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Locora.App.Services;

public sealed class ShellContextMenuRegistrationService : IShellContextMenuRegistrationService
{
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly ILogger<ShellContextMenuRegistrationService> _logger;

    public ShellContextMenuRegistrationService(
        IEnvironmentPaths environmentPaths,
        ILogger<ShellContextMenuRegistrationService> logger)
    {
        _environmentPaths = environmentPaths;
        _logger = logger;
    }

    public ShellContextMenuRegistrationFiles RefreshRegistrationFiles()
    {
        Directory.CreateDirectory(_environmentPaths.ShellIntegrationRoot);

        var executablePath = ResolveExecutablePath();
        File.WriteAllText(_environmentPaths.ShellContextMenuInstallFile, BuildInstallRegistryFile(executablePath), Encoding.Unicode);
        File.WriteAllText(_environmentPaths.ShellContextMenuUninstallFile, BuildUninstallRegistryFile(), Encoding.Unicode);

        var readmePath = Path.Combine(_environmentPaths.ShellIntegrationRoot, "README.txt");
        File.WriteAllText(readmePath, BuildReadme(executablePath), Encoding.UTF8);

        _logger.LogInformation(
            "Shell context menu registration files refreshed at {Root}.",
            _environmentPaths.ShellIntegrationRoot);

        return new ShellContextMenuRegistrationFiles(
            _environmentPaths.ShellIntegrationRoot,
            _environmentPaths.ShellContextMenuInstallFile,
            _environmentPaths.ShellContextMenuUninstallFile,
            readmePath,
            executablePath);
    }

    private static string ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(mainModulePath) && File.Exists(mainModulePath))
        {
            return mainModulePath;
        }

        return Path.Combine(AppContext.BaseDirectory, "Locora.App.exe");
    }

    private static string BuildInstallRegistryFile(string executablePath)
    {
        var icon = RegistryString($"{executablePath},0");
        var folderOpenCommand = RegistryString(BuildAppCommand(executablePath, "open", "%1"));
        var folderRevealCommand = RegistryString(BuildAppCommand(executablePath, "reveal", "%1"));
        var folderEditorCommand = RegistryString(BuildAppCommand(executablePath, "editor", "%1"));
        var folderTerminalCommand = RegistryString(BuildAppCommand(executablePath, "terminal", "%1"));
        var backgroundOpenCommand = RegistryString(BuildAppCommand(executablePath, "open", "%V"));
        var backgroundRevealCommand = RegistryString(BuildAppCommand(executablePath, "reveal", "%V"));
        var backgroundEditorCommand = RegistryString(BuildAppCommand(executablePath, "editor", "%V"));
        var backgroundTerminalCommand = RegistryString(BuildAppCommand(executablePath, "terminal", "%V"));

        return $"""
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora]
"MUIVerb"="Locora"
"Icon"="{icon}"
"SubCommands"=""

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\open]
"MUIVerb"="Open Folder in Locora"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\open\command]
@="{folderOpenCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\reveal]
"MUIVerb"="Reveal in Explorer"
"Icon"="explorer.exe"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\reveal\command]
@="{folderRevealCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\editor]
"MUIVerb"="Open in Preferred Editor"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\editor\command]
@="{folderEditorCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\terminal]
"MUIVerb"="Open Locora Terminal Here"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora\shell\terminal\command]
@="{folderTerminalCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora]
"MUIVerb"="Locora"
"Icon"="{icon}"
"SubCommands"=""

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\open]
"MUIVerb"="Open Folder in Locora"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\open\command]
@="{backgroundOpenCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\reveal]
"MUIVerb"="Reveal in Explorer"
"Icon"="explorer.exe"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\reveal\command]
@="{backgroundRevealCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\editor]
"MUIVerb"="Open in Preferred Editor"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\editor\command]
@="{backgroundEditorCommand}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\terminal]
"MUIVerb"="Open Locora Terminal Here"
"Icon"="{icon}"

[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora\shell\terminal\command]
@="{backgroundTerminalCommand}"
""";
    }

    private static string BuildUninstallRegistryFile()
    {
        return """
Windows Registry Editor Version 5.00

[-HKEY_CURRENT_USER\Software\Classes\Directory\shell\Locora]
[-HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\Locora]
""";
    }

    private static string BuildReadme(string executablePath)
    {
        return $"""
Locora shell context menu registration

Generated executable:
{executablePath}

Install:
Double-click install-context-menu.reg and accept the Windows Registry Editor prompt.

Uninstall:
Double-click uninstall-context-menu.reg and accept the Windows Registry Editor prompt.

The registration is stored under HKCU\Software\Classes, so it targets the current Windows user.
Regenerate these files after moving or publishing Locora, because the commands include the current executable path.
""";
    }

    private static string BuildAppCommand(string executablePath, string action, string targetToken)
    {
        return $"\"{executablePath}\" {ShellContextMenuCommand.ArgumentPrefix} {action} \"{targetToken}\"";
    }

    private static string RegistryString(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
