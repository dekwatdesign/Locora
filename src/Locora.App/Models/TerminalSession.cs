using CommunityToolkit.Mvvm.ComponentModel;

namespace Locora.App.Models;

public sealed class TerminalSession : ObservableObject
{
    private const int MaxOutputLength = 120_000;
    private string _output = string.Empty;
    private string _pendingInput = string.Empty;
    private bool _isRunning;
    private int? _exitCode;
    private DateTimeOffset _lastActivityAt;

    public TerminalSession(
        string id,
        string title,
        ProjectTerminalLaunchProfile launchProfile,
        string backend,
        DateTimeOffset startedAt)
    {
        Id = id;
        Title = title;
        LaunchProfile = launchProfile;
        WorkingDirectory = launchProfile.WorkingDirectory;
        Backend = backend;
        StartedAt = startedAt;
        _lastActivityAt = startedAt;
    }

    public string Id { get; }

    public string Title { get; }

    public ProjectTerminalLaunchProfile LaunchProfile { get; }

    public string WorkingDirectory { get; }

    public string Backend { get; }

    public bool IsProjectScoped => LaunchProfile.EnvironmentVariables.ContainsKey("LOCORA_PROJECT_ROOT");

    public DateTimeOffset StartedAt { get; }

    public string StartedAtLabel => StartedAt.ToString("HH:mm:ss");

    public string LastActivityLabel => _lastActivityAt.ToString("HH:mm:ss");

    public string Output
    {
        get => _output;
        private set => SetProperty(ref _output, value);
    }

    public string PendingInput
    {
        get => _pendingInput;
        set => SetProperty(ref _pendingInput, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public int? ExitCode
    {
        get => _exitCode;
        private set
        {
            if (SetProperty(ref _exitCode, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string Status => IsRunning
        ? $"{Backend} running"
        : ExitCode.HasValue
            ? $"{Backend} exited with code {ExitCode.Value}"
            : $"{Backend} stopped";

    public string CloseAutomationName => $"Close terminal tab {Title}";

    internal void MarkStarted()
    {
        IsRunning = true;
        ExitCode = null;
    }

    internal void MarkExited(int? exitCode)
    {
        ExitCode = exitCode;
        IsRunning = false;
    }

    internal void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var nextOutput = Output + text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (nextOutput.Length > MaxOutputLength)
        {
            nextOutput = nextOutput[^MaxOutputLength..];
        }

        Output = nextOutput;
        _lastActivityAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(LastActivityLabel));
    }
}
