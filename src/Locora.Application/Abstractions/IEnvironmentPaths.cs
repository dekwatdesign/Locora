namespace Locora.Application.Abstractions;

public interface IEnvironmentPaths
{
    string AppRoot { get; }

    string UserRoot { get; }

    string CacheRoot { get; }

    string ConfigRoot { get; }

    string AliasesRoot { get; }

    string ShellIntegrationRoot { get; }

    string LogsRoot { get; }

    string ProjectRoot { get; }

    string DataRoot { get; }

    string BinRoot { get; }

    string TempRoot { get; }

    string AppSettingsFile { get; }

    string SupervisorSettingsFile { get; }

    string ServicesSettingsFile { get; }

    string ProjectsSettingsFile { get; }

    string ProfilesSettingsFile { get; }

    string PackageSourcesSettingsFile { get; }

    string PackagesLockSettingsFile { get; }

    string ProjectPinsSettingsFile { get; }

    string OnboardingSettingsFile { get; }

    string TerminalCommandsSettingsFile { get; }

    string ShellContextMenuInstallFile { get; }

    string ShellContextMenuUninstallFile { get; }

    string PackageManifestsRoot { get; }

    string PackageCacheRoot { get; }

    string GetLogFilePath(string processName);

    string GetServiceOutputLogPath(string serviceKey);

    string GetServiceErrorLogPath(string serviceKey);
}
