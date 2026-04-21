namespace Locora.Application.Abstractions;

public interface IEnvironmentPaths
{
    string AppRoot { get; }

    string UserRoot { get; }

    string ConfigRoot { get; }

    string LogsRoot { get; }

    string ProjectRoot { get; }

    string DataRoot { get; }

    string BinRoot { get; }

    string TempRoot { get; }

    string AppSettingsFile { get; }

    string SupervisorSettingsFile { get; }

    string ServicesSettingsFile { get; }

    string ProjectsSettingsFile { get; }

    string GetLogFilePath(string processName);

    string GetServiceOutputLogPath(string serviceKey);

    string GetServiceErrorLogPath(string serviceKey);
}
