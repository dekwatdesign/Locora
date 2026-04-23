using System.Diagnostics;
using System.Linq;
using System.Text;
using Locora.App.Models;
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

    public void RevealInExplorer(string path)
    {
        var targetPath = Path.GetFullPath(path);
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Explorer target does not exist: {targetPath}", targetPath);
        }

        var workingDirectory = Directory.Exists(targetPath)
            ? Directory.GetParent(targetPath)?.FullName ?? targetPath
            : Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;

        Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{targetPath}\"",
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        });
    }

    public void OpenTerminal(ProjectTerminalLaunchProfile launchProfile)
    {
        EnsureDirectoryExists(launchProfile.WorkingDirectory);
        StartFirstAvailable(GetTerminalCandidates(launchProfile), "No supported terminal executable was found.");
    }

    public void OpenDatabaseAdminTool(string serviceKey, string workingDirectory)
    {
        EnsureDirectoryExists(workingDirectory);
        StartFirstAvailable(
            GetDatabaseAdminToolCandidates(serviceKey, workingDirectory),
            $"No supported database admin tool executable was found for service '{serviceKey}'.");
    }

    public void OpenEditor(string folderPath)
    {
        EnsureDirectoryExists(folderPath);
        var context = CreateEditorLaunchContext(folderPath);
        StartFirstAvailable(GetEditorCandidates(context), "No supported editor executable was found.");
    }

    private IEnumerable<ProcessStartInfo> GetTerminalCandidates(ProjectTerminalLaunchProfile launchProfile)
    {
        foreach (var candidate in GetPreferredTerminalCandidates(launchProfile))
        {
            yield return candidate;
        }

        yield return CreateTerminalStartInfo("wt.exe", launchProfile, argument: launchProfile.WorkingDirectory, flag: "-d");
        yield return CreateTerminalStartInfo("pwsh.exe", launchProfile);
        yield return CreateTerminalStartInfo("powershell.exe", launchProfile);
        yield return CreateTerminalStartInfo("cmd.exe", launchProfile);
    }

    private IEnumerable<ProcessStartInfo> GetPreferredTerminalCandidates(ProjectTerminalLaunchProfile launchProfile)
    {
        var preferredShell = _experience.PreferredShell.Trim();
        if (string.IsNullOrWhiteSpace(preferredShell))
        {
            yield break;
        }

        if (preferredShell.Contains("terminal", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateTerminalStartInfo("wt.exe", launchProfile, argument: launchProfile.WorkingDirectory, flag: "-d");
            yield break;
        }

        if (preferredShell.Contains("power", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateTerminalStartInfo("pwsh.exe", launchProfile);
            yield return CreateTerminalStartInfo("powershell.exe", launchProfile);
            yield break;
        }

        if (preferredShell.Contains("cmd", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateTerminalStartInfo("cmd.exe", launchProfile);
            yield break;
        }

        yield return CreateTerminalStartInfo(preferredShell, launchProfile);
    }

    private IEnumerable<ProcessStartInfo> GetDatabaseAdminToolCandidates(string serviceKey, string workingDirectory)
    {
        foreach (var candidate in GetRegisteredDatabaseAdminToolCandidates(serviceKey, workingDirectory))
        {
            yield return candidate;
        }

        foreach (var candidate in GetInstalledDatabaseAdminToolCandidates(serviceKey, workingDirectory))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<ProcessStartInfo> GetRegisteredDatabaseAdminToolCandidates(string serviceKey, string workingDirectory)
    {
        foreach (var executable in GetDatabaseAdminExecutableNames(serviceKey))
        {
            yield return CreateStartInfo(executable, workingDirectory);
        }
    }

    private static IEnumerable<ProcessStartInfo> GetInstalledDatabaseAdminToolCandidates(string serviceKey, string workingDirectory)
    {
        foreach (var path in GetDatabaseAdminExecutablePaths(serviceKey))
        {
            if (File.Exists(path))
            {
                yield return CreateStartInfo(path, workingDirectory);
            }
        }
    }

    private static IEnumerable<string> GetDatabaseAdminExecutableNames(string serviceKey)
    {
        if (serviceKey.Equals("mariadb", StringComparison.OrdinalIgnoreCase) ||
            serviceKey.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            yield return "MySQLWorkbench.exe";
            yield return "mysqlworkbench.exe";
            yield return "heidisql.exe";
        }
        else if (serviceKey.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            serviceKey.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            yield return "pgAdmin4.exe";
            yield return "pgadmin4.exe";
        }

        yield return "dbeaver.exe";
        yield return "azuredatastudio.exe";
        yield return "dbgate.exe";
    }

    private static IEnumerable<string> GetDatabaseAdminExecutablePaths(string serviceKey)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (serviceKey.Equals("mariadb", StringComparison.OrdinalIgnoreCase) ||
            serviceKey.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "MySQL", "MySQL Workbench 8.0 CE", "MySQLWorkbench.exe");
                yield return Path.Combine(programFiles, "HeidiSQL", "heidisql.exe");
                yield return Path.Combine(programFiles, "DBeaver", "dbeaver.exe");
            }

            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "HeidiSQL", "heidisql.exe");
            }
        }
        else if (serviceKey.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            serviceKey.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "pgAdmin 4", "runtime", "pgAdmin4.exe");
                yield return Path.Combine(programFiles, "DBeaver", "dbeaver.exe");
            }
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Azure Data Studio", "azuredatastudio.exe");
            yield return Path.Combine(localAppData, "Programs", "DbGate", "dbgate.exe");
        }
    }

    private IEnumerable<ProcessStartInfo> GetEditorCandidates(EditorLaunchContext context)
    {
        foreach (var candidate in GetPreferredEditorCandidates(context))
        {
            yield return candidate;
        }

        foreach (var candidate in GetCodeEditorCandidates(context, "code-insiders.cmd", "code-insiders.exe"))
        {
            yield return candidate;
        }

        foreach (var candidate in GetCodeEditorCandidates(context, "code.cmd", "code.exe"))
        {
            yield return candidate;
        }

        foreach (var candidate in GetCodeEditorCandidates(context, "cursor.exe", "cursor.cmd"))
        {
            yield return candidate;
        }

        foreach (var candidate in GetCodeEditorCandidates(context, "windsurf.exe", "windsurf.cmd"))
        {
            yield return candidate;
        }

        foreach (var candidate in GetCodeEditorCandidates(context, "codium.exe", "codium.cmd"))
        {
            yield return candidate;
        }

        foreach (var candidate in GetVisualStudioEditorCandidates(context))
        {
            yield return candidate;
        }

        foreach (var candidate in GetJetBrainsEditorCandidates(context, "rider64.exe", "rider.exe", "idea64.exe", "webstorm64.exe", "phpstorm64.exe", "pycharm64.exe"))
        {
            yield return candidate;
        }
    }

    private IEnumerable<ProcessStartInfo> GetPreferredEditorCandidates(EditorLaunchContext context)
    {
        var preferredEditor = _experience.PreferredEditor.Trim();
        if (string.IsNullOrWhiteSpace(preferredEditor))
        {
            yield break;
        }

        if (preferredEditor.Contains("cursor", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetCodeEditorCandidates(context, "cursor.exe", "cursor.cmd"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("windsurf", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetCodeEditorCandidates(context, "windsurf.exe", "windsurf.cmd"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("codium", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetCodeEditorCandidates(context, "codium.exe", "codium.cmd"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("insiders", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetCodeEditorCandidates(context, "code-insiders.cmd", "code-insiders.exe"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetCodeEditorCandidates(context, "code.cmd", "code.exe"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("rider", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("webstorm", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("phpstorm", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("pycharm", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("idea", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetJetBrainsEditorCandidates(context, "rider64.exe", "rider.exe", "idea64.exe", "webstorm64.exe", "phpstorm64.exe", "pycharm64.exe"))
            {
                yield return candidate;
            }

            yield break;
        }

        if (preferredEditor.Contains("visual studio", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in GetVisualStudioEditorCandidates(context))
            {
                yield return candidate;
            }

            yield break;
        }

        if (LooksLikeEditorCommandTemplate(preferredEditor))
        {
            var templateStartInfo = CreateEditorCommandTemplateStartInfo(preferredEditor, context);
            if (templateStartInfo is not null)
            {
                yield return templateStartInfo;
            }

            yield break;
        }

        yield return CreateStartInfo(preferredEditor, context.WorkingDirectory, context.PrimaryTargetPath);
    }

    private static IEnumerable<ProcessStartInfo> GetCodeEditorCandidates(EditorLaunchContext context, params string[] executableNames)
    {
        foreach (var executableName in executableNames)
        {
            yield return CreateStartInfo(executableName, context.WorkingDirectory, context.CodeTargetPath);
        }

        foreach (var installedPath in GetInstalledEditorPaths(executableNames))
        {
            yield return CreateStartInfo(installedPath, context.WorkingDirectory, context.CodeTargetPath);
        }
    }

    private static IEnumerable<ProcessStartInfo> GetJetBrainsEditorCandidates(EditorLaunchContext context, params string[] executableNames)
    {
        foreach (var executableName in executableNames)
        {
            yield return CreateStartInfo(executableName, context.WorkingDirectory, context.IdeTargetPath);
        }

        foreach (var installedPath in GetInstalledJetBrainsPaths(executableNames))
        {
            yield return CreateStartInfo(installedPath, context.WorkingDirectory, context.IdeTargetPath);
        }
    }

    private static IEnumerable<ProcessStartInfo> GetVisualStudioEditorCandidates(EditorLaunchContext context)
    {
        if (context.SolutionOrProjectPath is null)
        {
            yield break;
        }

        yield return CreateStartInfo("devenv.exe", context.WorkingDirectory, context.SolutionOrProjectPath);

        foreach (var installedPath in GetInstalledVisualStudioPaths())
        {
            yield return CreateStartInfo(installedPath, context.WorkingDirectory, context.SolutionOrProjectPath);
        }
    }

    private static IEnumerable<string> GetInstalledEditorPaths(IEnumerable<string> executableNames)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        foreach (var executableName in executableNames)
        {
            if (string.IsNullOrWhiteSpace(executableName))
            {
                continue;
            }

            foreach (var directory in GetKnownEditorInstallDirectories(executableName, localAppData, programFiles))
            {
                var candidatePath = Path.Combine(directory, executableName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                    ? executableName.Replace(".cmd", ".exe", StringComparison.OrdinalIgnoreCase)
                    : executableName);
                if (File.Exists(candidatePath))
                {
                    yield return candidatePath;
                }
            }
        }
    }

    private static IEnumerable<string> GetInstalledJetBrainsPaths(IEnumerable<string> executableNames)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        foreach (var executableName in executableNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(executableName))
            {
                continue;
            }

            foreach (var root in GetJetBrainsInstallRoots(localAppData, programFiles))
            {
                foreach (var candidatePath in TryEnumerateFiles(root, executableName))
                {
                    yield return candidatePath;
                }
            }
        }
    }

    private static IEnumerable<string> GetInstalledVisualStudioPaths()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio")
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var yearDirectory in TryEnumerateDirectories(root))
            {
                foreach (var editionDirectory in TryEnumerateDirectories(yearDirectory))
                {
                    var candidatePath = Path.Combine(editionDirectory, "Common7", "IDE", "devenv.exe");
                    if (File.Exists(candidatePath))
                    {
                        yield return candidatePath;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetKnownEditorInstallDirectories(string executableName, string localAppData, string programFiles)
    {
        if (executableName.Contains("code-insiders", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code Insiders");
            }
        }
        else if (executableName.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code");
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Microsoft VS Code");
            }
        }
        else if (executableName.Contains("cursor", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Cursor");
            }
        }
        else if (executableName.Contains("windsurf", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Windsurf");
            }
        }
        else if (executableName.Contains("codium", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "VSCodium");
            }
        }
    }

    private static IEnumerable<string> GetJetBrainsInstallRoots(string localAppData, string programFiles)
    {
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs");
            yield return Path.Combine(localAppData, "JetBrains", "Toolbox", "apps");
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "JetBrains");
        }
    }

    private static IEnumerable<string> TryEnumerateDirectories(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        IEnumerator<string>? directories = null;
        try
        {
            directories = Directory.EnumerateDirectories(root).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (directories)
        {
            while (true)
            {
                string currentDirectory;
                try
                {
                    if (!directories.MoveNext())
                    {
                        break;
                    }

                    currentDirectory = directories.Current;
                }
                catch (Exception)
                {
                    yield break;
                }

                yield return currentDirectory;
            }
        }
    }

    private static IEnumerable<string> TryEnumerateFiles(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        IEnumerator<string>? files = null;
        try
        {
            files = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (files)
        {
            while (true)
            {
                string currentFile;
                try
                {
                    if (!files.MoveNext())
                    {
                        break;
                    }

                    currentFile = files.Current;
                }
                catch (Exception)
                {
                    yield break;
                }

                yield return currentFile;
            }
        }
    }

    private static bool LooksLikeEditorCommandTemplate(string preferredEditor)
    {
        return preferredEditor.Contains('"') ||
            preferredEditor.Contains("{editorTarget}", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("{workspace}", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("{solution}", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("{projectFile}", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains("{folder}", StringComparison.OrdinalIgnoreCase) ||
            preferredEditor.Contains(' ');
    }

    private static ProcessStartInfo? CreateEditorCommandTemplateStartInfo(string template, EditorLaunchContext context)
    {
        var tokens = SplitCommandLine(template)
            .Select(token => ReplaceEditorTemplateToken(token, context))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        var startInfo = CreateStartInfo(tokens[0], context.WorkingDirectory, tokens.Skip(1));
        if (!template.Contains("{editorTarget}", StringComparison.OrdinalIgnoreCase) &&
            !template.Contains("{workspace}", StringComparison.OrdinalIgnoreCase) &&
            !template.Contains("{solution}", StringComparison.OrdinalIgnoreCase) &&
            !template.Contains("{projectFile}", StringComparison.OrdinalIgnoreCase) &&
            !template.Contains("{folder}", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(context.PrimaryTargetPath);
        }

        return startInfo;
    }

    private static string ReplaceEditorTemplateToken(string token, EditorLaunchContext context)
    {
        return token
            .Replace("{editorTarget}", context.PrimaryTargetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{workspace}", context.CodeTargetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{solution}", context.SolutionPath ?? context.SolutionOrProjectPath ?? context.FolderPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{projectFile}", context.ProjectFilePath ?? context.SolutionOrProjectPath ?? context.FolderPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{folder}", context.FolderPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{project}", context.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static EditorLaunchContext CreateEditorLaunchContext(string folderPath)
    {
        var workspacePath = Directory.EnumerateFiles(folderPath, "*.code-workspace", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var solutionPath = Directory.EnumerateFiles(folderPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(folderPath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var projectFiles = Directory.EnumerateFiles(folderPath, "*.*proj", SearchOption.TopDirectoryOnly)
            .Where(path =>
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projectFilePath = projectFiles.Length == 1 ? projectFiles[0] : null;

        return new EditorLaunchContext(folderPath, workspacePath, solutionPath, projectFilePath);
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
        return CreateStartInfo(
            fileName,
            workingDirectory,
            new[]
            {
                flag,
                argument
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!));
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string workingDirectory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateTerminalStartInfo(
        string fileName,
        ProjectTerminalLaunchProfile launchProfile,
        string? argument = null,
        string? flag = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            WorkingDirectory = launchProfile.WorkingDirectory
        };

        if (!string.IsNullOrWhiteSpace(flag))
        {
            startInfo.ArgumentList.Add(flag);
        }

        if (!string.IsNullOrWhiteSpace(argument))
        {
            startInfo.ArgumentList.Add(argument);
        }

        TerminalLaunchEnvironment.Apply(startInfo, launchProfile);
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

    private sealed record EditorLaunchContext(
        string FolderPath,
        string? WorkspacePath,
        string? SolutionPath,
        string? ProjectFilePath)
    {
        public string WorkingDirectory => FolderPath;

        public string CodeTargetPath => WorkspacePath ?? FolderPath;

        public string? SolutionOrProjectPath => SolutionPath ?? ProjectFilePath;

        public string IdeTargetPath => SolutionOrProjectPath ?? FolderPath;

        public string PrimaryTargetPath => WorkspacePath ?? SolutionOrProjectPath ?? FolderPath;
    }
}
