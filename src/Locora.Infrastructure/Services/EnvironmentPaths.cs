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

    public string AliasesRoot => Path.Combine(UserRoot, "aliases");

    public string ShellIntegrationRoot => Path.Combine(UserRoot, "shell");

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
