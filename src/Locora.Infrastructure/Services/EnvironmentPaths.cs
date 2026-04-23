using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Locora.Infrastructure.Services;

public sealed class EnvironmentPaths : IEnvironmentPaths
{
    private readonly DirectorySettings _directories;
    private readonly Lazy<string> _appRoot;

    public EnvironmentPaths(IOptions<AppSettings> settings)
    {
        _directories = settings.Value.Directories;
        _appRoot = new Lazy<string>(ResolveAppRoot);
    }

    public string AppRoot => _appRoot.Value;

    public string UserRoot => Path.Combine(AppRoot, _directories.UserRootName);

    public string CacheRoot => Path.Combine(UserRoot, "cache");

    public string ConfigRoot => Path.Combine(UserRoot, "config");

    public string ConfigBackupRoot => Path.Combine(UserRoot, "backups", "config");

    public string ProfilesRoot => Path.Combine(UserRoot, "profiles");

    public string AliasesRoot => Path.Combine(UserRoot, "aliases");

    public string ShellIntegrationRoot => Path.Combine(UserRoot, "shell");

    public string UserEnvironmentApplyScriptFile => Path.Combine(ShellIntegrationRoot, "install-user-environment.ps1");

    public string UserEnvironmentRemoveScriptFile => Path.Combine(ShellIntegrationRoot, "uninstall-user-environment.ps1");

    public string UserEnvironmentManifestFile => Path.Combine(ShellIntegrationRoot, "user-environment-manifest.json");

    public string UserEnvironmentBackupRoot => Path.Combine(ShellIntegrationRoot, "environment-backups");

    public string AppUpdateRoot => Path.Combine(UserRoot, "updates");

    public string AppUpdateDownloadRoot => Path.Combine(AppUpdateRoot, "downloads");

    public string AppUpdateManifestCacheFile => Path.Combine(AppUpdateRoot, "latest-release.json");

    public string AppUpdatePlanFile => Path.Combine(AppUpdateRoot, "update-plan.json");

    public string AppUpdateManifestExampleFile => Path.Combine(AppUpdateRoot, "release-manifest.example.json");

    public string PortableDistributionRoot => Path.Combine(UserRoot, "distribution");

    public string PortableDistributionArtifactsRoot => Path.Combine(PortableDistributionRoot, "artifacts");

    public string PortableDistributionScriptFile => Path.Combine(PortableDistributionRoot, "build-portable-distribution.ps1");

    public string PortableDistributionPlanFile => Path.Combine(PortableDistributionRoot, "portable-distribution-plan.json");

    public string PortableDistributionReadmeFile => Path.Combine(PortableDistributionRoot, "README.md");

    public string PortableDistributionManifestTemplateFile => Path.Combine(PortableDistributionRoot, "release-manifest.template.json");

    public string LogsRoot => Path.Combine(UserRoot, "logs");

    public string ProjectRoot => Path.Combine(AppRoot, _directories.ProjectRootName);

    public string DataRoot => Path.Combine(AppRoot, _directories.DataRootName);

    public string BinRoot => Path.Combine(AppRoot, _directories.BinRootName);

    public string TempRoot => Path.Combine(AppRoot, _directories.TempRootName);

    public string AppSettingsFile => Path.Combine(ConfigRoot, "appsettings.json");

    public string SupervisorSettingsFile => Path.Combine(ConfigRoot, "supervisor.json");

    public string ServicesSettingsFile => Path.Combine(ConfigRoot, "services.json");

    public string ProjectsSettingsFile => Path.Combine(ConfigRoot, "projects.json");

    public string ProfilesSettingsFile => Path.Combine(ConfigRoot, "profiles.json");

    public string PackageSourcesSettingsFile => Path.Combine(ConfigRoot, "sources.json");

    public string PackagesLockSettingsFile => Path.Combine(ConfigRoot, "packages.lock.json");

    public string ProjectPinsSettingsFile => Path.Combine(ConfigRoot, "project-pins.json");

    public string OnboardingSettingsFile => Path.Combine(ConfigRoot, "onboarding.json");

    public string TerminalCommandsSettingsFile => Path.Combine(ConfigRoot, "terminal-commands.json");

    public string CustomToolsSettingsFile => Path.Combine(ConfigRoot, "custom-tools.json");

    public string LocalTunnelsSettingsFile => Path.Combine(ConfigRoot, "local-tunnels.json");

    public string ShellContextMenuInstallFile => Path.Combine(ShellIntegrationRoot, "install-context-menu.reg");

    public string ShellContextMenuUninstallFile => Path.Combine(ShellIntegrationRoot, "uninstall-context-menu.reg");

    public string PackageManifestsRoot => Path.Combine(AppRoot, "package-manifests");

    public string PackageCacheRoot => Path.Combine(CacheRoot, "packages");

    public string GetLogFilePath(string processName) => Path.Combine(LogsRoot, $"{processName}.log.jsonl");

    public string GetServiceOutputLogPath(string serviceKey) => Path.Combine(LogsRoot, $"{serviceKey}.stdout.log");

    public string GetServiceErrorLogPath(string serviceKey) => Path.Combine(LogsRoot, $"{serviceKey}.stderr.log");

    private static string ResolveAppRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }
}
