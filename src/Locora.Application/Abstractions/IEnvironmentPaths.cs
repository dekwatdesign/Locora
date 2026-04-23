namespace Locora.Application.Abstractions;

public interface IEnvironmentPaths
{
    string AppRoot { get; }

    string UserRoot { get; }

    string CacheRoot { get; }

    string ConfigRoot { get; }

    string ConfigBackupRoot { get; }

    string ProfilesRoot { get; }

    string AliasesRoot { get; }

    string ShellIntegrationRoot { get; }

    string UserEnvironmentApplyScriptFile { get; }

    string UserEnvironmentRemoveScriptFile { get; }

    string UserEnvironmentManifestFile { get; }

    string UserEnvironmentBackupRoot { get; }

    string AppUpdateRoot { get; }

    string AppUpdateDownloadRoot { get; }

    string AppUpdateManifestCacheFile { get; }

    string AppUpdatePlanFile { get; }

    string AppUpdateManifestExampleFile { get; }

    string PortableDistributionRoot { get; }

    string PortableDistributionArtifactsRoot { get; }

    string PortableDistributionScriptFile { get; }

    string PortableDistributionPlanFile { get; }

    string PortableDistributionReadmeFile { get; }

    string PortableDistributionManifestTemplateFile { get; }

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

    string CustomToolsSettingsFile { get; }

    string LocalTunnelsSettingsFile { get; }

    string ShellContextMenuInstallFile { get; }

    string ShellContextMenuUninstallFile { get; }

    string PackageManifestsRoot { get; }

    string PackageCacheRoot { get; }

    string GetLogFilePath(string processName);

    string GetServiceOutputLogPath(string serviceKey);

    string GetServiceErrorLogPath(string serviceKey);
}
