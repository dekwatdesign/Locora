using System.Diagnostics;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.App.Services;

public sealed class ProjectActionLauncher : IProjectActionLauncher
{
    private readonly ExperienceSettings _experience;
    private readonly ILogger<ProjectActionLauncher> _logger;

    public ProjectActionLauncher(
        IOptions<AppSettings> settings,
        ILogger<ProjectActionLauncher> logger)
    {
        _experience = settings.Value.Experience;
        _logger = logger;
    }

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Project URL is empty.");
        }

        Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public void OpenFile(string filePath)
    {
        EnsureFileExists(filePath);

        Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory
        });
    }

    public void OpenFolder(string folderPath)
    {
        EnsureDirectoryExists(folderPath);
        Start(CreateStartInfo("explorer.exe", folderPath, argument: folderPath));
    }

    public void OpenTerminal(string folderPath)
    {
        EnsureDirectoryExists(folderPath);
        StartFirstAvailable(GetTerminalCandidates(folderPath), "No supported terminal executable was found.");
    }

    public void OpenEditor(string folderPath)
    {
        EnsureDirectoryExists(folderPath);
        StartFirstAvailable(GetEditorCandidates(folderPath), "No supported editor executable was found.");
    }

    private IEnumerable<ProcessStartInfo> GetTerminalCandidates(string folderPath)
    {
        foreach (var candidate in GetPreferredTerminalCandidates(folderPath))
        {
            yield return candidate;
        }

        yield return CreateStartInfo("wt.exe", folderPath, argument: folderPath, flag: "-d");
        yield return CreateStartInfo("pwsh.exe", folderPath);
        yield return CreateStartInfo("powershell.exe", folderPath);
        yield return CreateStartInfo("cmd.exe", folderPath);
    }

    private IEnumerable<ProcessStartInfo> GetPreferredTerminalCandidates(string folderPath)
    {
        var preferredShell = _experience.PreferredShell.Trim();
        if (string.IsNullOrWhiteSpace(preferredShell))
        {
            yield break;
        }

        if (preferredShell.Contains("terminal", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStartInfo("wt.exe", folderPath, argument: folderPath, flag: "-d");
            yield break;
        }

        if (preferredShell.Contains("power", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStartInfo("pwsh.exe", folderPath);
            yield return CreateStartInfo("powershell.exe", folderPath);
            yield break;
        }

        if (preferredShell.Contains("cmd", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStartInfo("cmd.exe", folderPath);
            yield break;
        }

        yield return CreateStartInfo(preferredShell, folderPath);
    }

    private IEnumerable<ProcessStartInfo> GetEditorCandidates(string folderPath)
    {
        foreach (var candidate in GetPreferredEditorCandidates(folderPath))
        {
            yield return candidate;
        }

        yield return CreateStartInfo("code.cmd", folderPath, argument: folderPath);
        yield return CreateStartInfo("code.exe", folderPath, argument: folderPath);

        var solutionPath = Directory.EnumerateFiles(folderPath, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solutionPath is not null)
        {
            yield return CreateStartInfo("devenv.exe", folderPath, argument: solutionPath);
        }
    }

    private IEnumerable<ProcessStartInfo> GetPreferredEditorCandidates(string folderPath)
    {
        var preferredEditor = _experience.PreferredEditor.Trim();
        if (string.IsNullOrWhiteSpace(preferredEditor))
        {
            yield break;
        }

        if (preferredEditor.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStartInfo("code.cmd", folderPath, argument: folderPath);
            yield return CreateStartInfo("code.exe", folderPath, argument: folderPath);
            yield break;
        }

        if (preferredEditor.Contains("visual studio", StringComparison.OrdinalIgnoreCase))
        {
            var solutionPath = Directory.EnumerateFiles(folderPath, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (solutionPath is not null)
            {
                yield return CreateStartInfo("devenv.exe", folderPath, argument: solutionPath);
            }

            yield break;
        }

        yield return CreateStartInfo(preferredEditor, folderPath, argument: folderPath);
    }

    private void StartFirstAvailable(IEnumerable<ProcessStartInfo> candidates, string failureMessage)
    {
        var attempted = new List<string>();

        foreach (var candidate in candidates)
        {
            attempted.Add(candidate.FileName);

            try
            {
                Start(candidate);
                return;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to start candidate {FileName}.", candidate.FileName);
            }
        }

        throw new InvalidOperationException($"{failureMessage} Tried: {string.Join(", ", attempted.Distinct(StringComparer.OrdinalIgnoreCase))}.");
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string workingDirectory, string? argument = null, string? flag = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        if (!string.IsNullOrWhiteSpace(flag))
        {
            startInfo.ArgumentList.Add(flag);
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void EnsureDirectoryExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Project folder does not exist: {folderPath}");
        }
    }

    private static void EnsureFileExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File does not exist: {filePath}", filePath);
        }
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start process '{startInfo.FileName}'.");
        }
    }
}
