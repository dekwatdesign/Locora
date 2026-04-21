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

    public string ConfigRoot => Path.Combine(UserRoot, "config");

    public string LogsRoot => Path.Combine(UserRoot, "logs");

    public string ProjectRoot => Path.Combine(AppRoot, _directories.ProjectRootName);

    public string DataRoot => Path.Combine(AppRoot, _directories.DataRootName);

    public string BinRoot => Path.Combine(AppRoot, _directories.BinRootName);

    public string TempRoot => Path.Combine(AppRoot, _directories.TempRootName);

    public string AppSettingsFile => Path.Combine(ConfigRoot, "appsettings.json");

    public string SupervisorSettingsFile => Path.Combine(ConfigRoot, "supervisor.json");

    public string ServicesSettingsFile => Path.Combine(ConfigRoot, "services.json");

    public string ProjectsSettingsFile => Path.Combine(ConfigRoot, "projects.json");

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
