using System.Diagnostics;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public abstract class ManagedServiceBase : IManagedService
{
    private readonly IEnvironmentPaths _paths;
    private readonly PortProbe _portProbe;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();
    private Process? _trackedProcess;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    private string? _lastNote;
    private int? _lastExitCode;
    private DateTimeOffset? _restartScheduledAt;
    private DateTimeOffset? _restartWindowStartedAt;
    private int _restartAttempts;
    private int _restartPlanVersion;
    private bool _restartSuspended;
    private bool _stopRequested;
    private bool _hasUnexpectedExit;

    protected ManagedServiceBase(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger logger)
    {
        Definition = definition;
        _paths = paths;
        _portProbe = portProbe;
        _logger = logger;
    }

    public ManagedServiceDefinition Definition { get; }

    public virtual async Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var trackedProcess = await GetLiveTrackedProcessAsync();

        var executablePath = ResolvePath(Definition.RelativeExecutablePath);
        var executableExists = File.Exists(executablePath);
        var portResponsive = Definition.Port is int port && await _portProbe.IsPortResponsiveAsync(port, cancellationToken);
        var trackedRunning = trackedProcess is { HasExited: false };
        string? lastNote;
        int? lastExitCode;
        bool restartSuspended;
        bool hasUnexpectedExit;
        DateTimeOffset? restartScheduledAt;

        lock (_syncRoot)
        {
            lastNote = _lastNote;
            lastExitCode = _lastExitCode;
            restartSuspended = _restartSuspended;
            hasUnexpectedExit = _hasUnexpectedExit;
            restartScheduledAt = _restartScheduledAt;
        }

        var state = DetermineState(executableExists, trackedRunning, portResponsive, restartScheduledAt, restartSuspended, hasUnexpectedExit);
        var note = DetermineNote(
            executableExists,
            trackedRunning,
            portResponsive,
            executablePath,
            lastNote,
            lastExitCode,
            restartScheduledAt,
            restartSuspended,
            hasUnexpectedExit);

        return new ManagedServiceStatus(
            Definition.Key,
            Definition.DisplayName,
            Definition.Version,
            Definition.Port,
            state,
            Definition.AutoStart,
            note,
            executableExists,
            portResponsive,
            trackedRunning ? trackedProcess!.Id : null,
            executablePath);
    }

    public virtual Task StartAsync(CancellationToken cancellationToken = default)
        => StartCoreAsync(cancellationToken, initiatedByRecovery: false, expectedRestartPlanVersion: null);

    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var trackedProcess = await GetLiveTrackedProcessAsync();
            bool hadPendingRestart;

            lock (_syncRoot)
            {
                hadPendingRestart = _restartScheduledAt is not null;
                _restartPlanVersion++;
                _restartScheduledAt = null;
                _restartWindowStartedAt = null;
                _restartAttempts = 0;
                _restartSuspended = false;
                _stopRequested = trackedProcess is not null;
                _hasUnexpectedExit = false;
                _lastExitCode = null;
            }

            if (trackedProcess is { HasExited: false })
            {
                trackedProcess.Kill(entireProcessTree: true);
                await trackedProcess.WaitForExitAsync(cancellationToken);
                await HandleTrackedProcessExitAsync(trackedProcess);

                lock (_syncRoot)
                {
                    _lastNote = $"Stopped {Definition.DisplayName}.";
                }

                _logger.LogInformation("{Service} stopped", Definition.DisplayName);
                return;
            }

            if (Definition.StopArguments.Count > 0)
            {
                var stopProcess = new Process
                {
                    StartInfo = CreateStopInfo(),
                    EnableRaisingEvents = false
                };

                if (stopProcess.Start())
                {
                    await stopProcess.WaitForExitAsync(cancellationToken);
                    var exitCode = stopProcess.ExitCode;
                    stopProcess.Dispose();

                    lock (_syncRoot)
                    {
                        _stopRequested = false;
                        _lastExitCode = exitCode == 0 ? null : exitCode;
                        _lastNote = exitCode == 0
                            ? $"Sent stop command to {Definition.DisplayName}."
                            : $"Stop command for {Definition.DisplayName} exited with code {exitCode}.";
                    }

                    return;
                }
            }

            lock (_syncRoot)
            {
                _stopRequested = false;
                _lastNote = hadPendingRestart
                    ? $"Cancelled the scheduled restart for {Definition.DisplayName}."
                    : $"No tracked process for {Definition.DisplayName}.";
            }
        }
        catch (OperationCanceledException)
        {
            lock (_syncRoot)
            {
                _stopRequested = false;
            }

            throw;
        }
        catch (Exception exception)
        {
            lock (_syncRoot)
            {
                _stopRequested = false;
                _lastNote = exception.Message;
            }

            _logger.LogWarning(exception, "Failed to stop {Service}", Definition.DisplayName);
        }
    }

    protected virtual async Task WaitForHealthyStartAsync(Process process, CancellationToken cancellationToken)
    {
        if (Definition.Port is not int port)
        {
            MarkHealthyStart(null);
            return;
        }

        var timeout = TimeSpan.FromMilliseconds(Math.Max(Definition.StartTimeoutMs, 1000));
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            if (process.HasExited)
            {
                await HandleTrackedProcessExitAsync(process);
                return;
            }

            if (await _portProbe.IsPortResponsiveAsync(port, cancellationToken))
            {
                MarkHealthyStart(port);
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        lock (_syncRoot)
        {
            _lastNote = $"Process started but port {port} did not respond within {Definition.StartTimeoutMs} ms.";
        }
    }

    protected virtual ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePath(Definition.RelativeExecutablePath),
            WorkingDirectory = ResolveWorkingDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in Definition.Arguments)
        {
            startInfo.ArgumentList.Add(ExpandToken(argument));
        }

        foreach (var pair in Definition.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = ExpandToken(pair.Value);
        }

        return startInfo;
    }

    protected virtual ProcessStartInfo CreateStopInfo()
    {
        var stopExecutable = string.IsNullOrWhiteSpace(Definition.RelativeStopExecutablePath)
            ? Definition.RelativeExecutablePath
            : Definition.RelativeStopExecutablePath;

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePath(stopExecutable),
            WorkingDirectory = ResolveWorkingDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in Definition.StopArguments)
        {
            startInfo.ArgumentList.Add(ExpandToken(argument));
        }

        return startInfo;
    }

    protected string ResolvePath(string path)
    {
        var expanded = ExpandToken(path);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, expanded));
    }

    protected string ResolveWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(Definition.RelativeWorkingDirectory))
        {
            return Path.GetDirectoryName(ResolvePath(Definition.RelativeExecutablePath)) ?? _paths.AppRoot;
        }

        return ResolvePath(Definition.RelativeWorkingDirectory);
    }

    protected virtual string ExpandToken(string value)
    {
        return value
            .Replace("{root}", _paths.AppRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{bin}", _paths.BinRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{config}", _paths.ConfigRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{logs}", _paths.LogsRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{data}", _paths.DataRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{temp}", _paths.TempRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", Definition.Port?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{projects}", _paths.ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Process?> GetLiveTrackedProcessAsync()
    {
        Process? trackedProcess;

        lock (_syncRoot)
        {
            trackedProcess = _trackedProcess;
        }

        if (trackedProcess is not null && trackedProcess.HasExited)
        {
            await HandleTrackedProcessExitAsync(trackedProcess);

            lock (_syncRoot)
            {
                trackedProcess = _trackedProcess;
            }
        }

        return trackedProcess is { HasExited: false } ? trackedProcess : null;
    }

    private async Task StartCoreAsync(
        CancellationToken cancellationToken,
        bool initiatedByRecovery,
        int? expectedRestartPlanVersion)
    {
        try
        {
            if (!initiatedByRecovery)
            {
                lock (_syncRoot)
                {
                    _restartPlanVersion++;
                    _restartScheduledAt = null;
                    _restartWindowStartedAt = null;
                    _restartAttempts = 0;
                    _restartSuspended = false;
                    _stopRequested = false;
                    _hasUnexpectedExit = false;
                    _lastExitCode = null;
                }
            }

            var trackedProcess = await GetLiveTrackedProcessAsync();
            if (trackedProcess is { HasExited: false })
            {
                lock (_syncRoot)
                {
                    _lastNote = "Start skipped because the service is already active.";
                }

                return;
            }

            var executablePath = ResolvePath(Definition.RelativeExecutablePath);
            if (!File.Exists(executablePath))
            {
                lock (_syncRoot)
                {
                    _lastNote = $"Executable not found at {executablePath}";
                }

                return;
            }

            if (Definition.Port is int port && await _portProbe.IsPortResponsiveAsync(port, cancellationToken))
            {
                lock (_syncRoot)
                {
                    _lastNote = $"Start skipped because port {port} is already in use.";
                }

                return;
            }

            var startInfo = CreateStartInfo();
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.Exited += OnTrackedProcessExited;

            if (!process.Start())
            {
                lock (_syncRoot)
                {
                    _lastNote = $"Failed to start {Definition.DisplayName}.";
                }

                return;
            }

            var stdoutPump = PumpStreamAsync(process.StandardOutput, _paths.GetServiceOutputLogPath(Definition.Key), CancellationToken.None);
            var stderrPump = PumpStreamAsync(process.StandardError, _paths.GetServiceErrorLogPath(Definition.Key), CancellationToken.None);
            bool acceptProcess = true;

            lock (_syncRoot)
            {
                if (initiatedByRecovery &&
                    expectedRestartPlanVersion is int expectedVersion &&
                    expectedVersion != _restartPlanVersion)
                {
                    acceptProcess = false;
                }
                else if (_stopRequested)
                {
                    acceptProcess = false;
                }
                else
                {
                    _trackedProcess = process;
                    _stdoutPump = stdoutPump;
                    _stderrPump = stderrPump;
                    _lastNote = initiatedByRecovery
                        ? $"Restarted {Definition.DisplayName}, waiting for health checks."
                        : $"Started {Definition.DisplayName}.";
                    _lastExitCode = null;
                    _restartScheduledAt = null;
                    _stopRequested = false;
                }
            }

            if (!acceptProcess)
            {
                process.Exited -= OnTrackedProcessExited;
                await FinalizeCancelledStartAsync(process, stdoutPump, stderrPump);

                lock (_syncRoot)
                {
                    _lastNote = $"Cancelled the restart for {Definition.DisplayName}.";
                }

                return;
            }

            _logger.LogInformation(
                "{Service} started with PID {Pid}{Suffix}",
                Definition.DisplayName,
                process.Id,
                initiatedByRecovery ? " after crash recovery" : string.Empty);

            await WaitForHealthyStartAsync(process, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (_syncRoot)
            {
                _lastNote = exception.Message;
                _hasUnexpectedExit = true;
            }

            _logger.LogWarning(exception, "Failed to start {Service}", Definition.DisplayName);
        }
    }

    private string DetermineState(
        bool executableExists,
        bool trackedRunning,
        bool portResponsive,
        DateTimeOffset? restartScheduledAt,
        bool restartSuspended,
        bool hasUnexpectedExit)
    {
        if (!executableExists)
        {
            return "Error";
        }

        if (trackedRunning && Definition.Port is int && !portResponsive)
        {
            return "Starting";
        }

        if (!trackedRunning && portResponsive)
        {
            return "Error";
        }

        if (trackedRunning)
        {
            return "Running";
        }

        if (restartScheduledAt is not null)
        {
            return "Starting";
        }

        if (restartSuspended || hasUnexpectedExit)
        {
            return "Error";
        }

        return "Stopped";
    }

    private string DetermineNote(
        bool executableExists,
        bool trackedRunning,
        bool portResponsive,
        string executablePath,
        string? lastNote,
        int? lastExitCode,
        DateTimeOffset? restartScheduledAt,
        bool restartSuspended,
        bool hasUnexpectedExit)
    {
        if (!executableExists)
        {
            return $"Executable not found: {executablePath}";
        }

        if (trackedRunning && Definition.Port is int port && !portResponsive)
        {
            return lastNote ?? $"Process is running and waiting for port {port}.";
        }

        if (!trackedRunning && portResponsive)
        {
            return lastNote ?? "Port is responsive from a process not tracked by Locora.";
        }

        if (restartScheduledAt is not null)
        {
            return lastNote ?? $"Restart scheduled for {restartScheduledAt.Value.LocalDateTime:t}.";
        }

        if (restartSuspended)
        {
            return lastNote ?? "Crash recovery is suspended until the next manual start.";
        }

        if (hasUnexpectedExit && lastExitCode is int exitCode)
        {
            return lastNote ?? $"Process exited unexpectedly with code {exitCode}.";
        }

        if (hasUnexpectedExit)
        {
            return lastNote ?? "Process exited unexpectedly.";
        }

        return lastNote ?? (Definition.AutoStart ? "Configured to auto-start." : "Ready to start.");
    }

    private void OnTrackedProcessExited(object? sender, EventArgs args)
    {
        if (sender is Process process)
        {
            _ = HandleTrackedProcessExitAsync(process);
        }
    }

    private Task HandleTrackedProcessExitAsync(Process process)
    {
        try
        {
            Task? stdoutPump;
            Task? stderrPump;
            int exitCode;
            bool shouldScheduleRestart;

            lock (_syncRoot)
            {
                if (!ReferenceEquals(_trackedProcess, process))
                {
                    return Task.CompletedTask;
                }

                exitCode = process.ExitCode;
                shouldScheduleRestart = !_stopRequested;
                _trackedProcess = null;
                stdoutPump = _stdoutPump;
                stderrPump = _stderrPump;
                _stdoutPump = null;
                _stderrPump = null;
                _restartScheduledAt = null;
                _lastExitCode = shouldScheduleRestart ? exitCode : null;
                _hasUnexpectedExit = shouldScheduleRestart;
                _stopRequested = false;
            }

            _ = FinalizeExitedProcessAsync(process, stdoutPump, stderrPump);

            if (!shouldScheduleRestart)
            {
                return Task.CompletedTask;
            }

            _logger.LogWarning("{Service} exited unexpectedly with code {ExitCode}", Definition.DisplayName, exitCode);
            ScheduleCrashRecovery(exitCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to handle exit for {Service}", Definition.DisplayName);
        }

        return Task.CompletedTask;
    }

    private void ScheduleCrashRecovery(int exitCode)
    {
        if (!Definition.RestartOnCrash)
        {
            lock (_syncRoot)
            {
                _lastNote = $"Process exited unexpectedly with code {exitCode}. Automatic restart is disabled.";
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        var restartWindow = TimeSpan.FromMilliseconds(Math.Max(Definition.RestartWindowMs, 1000));
        var allowedAttempts = Math.Max(Definition.MaxRestartAttempts, 1);
        DateTimeOffset restartAt = default;
        TimeSpan delay;
        int restartPlanVersion = 0;
        int attempt;
        bool recoverySuspended;

        lock (_syncRoot)
        {
            if (_restartWindowStartedAt is null || now - _restartWindowStartedAt > restartWindow)
            {
                _restartWindowStartedAt = now;
                _restartAttempts = 0;
            }

            _restartAttempts++;
            attempt = _restartAttempts;
            recoverySuspended = attempt > allowedAttempts;

            if (recoverySuspended)
            {
                _restartScheduledAt = null;
                _restartSuspended = true;
                _lastNote = $"Crash recovery suspended after {allowedAttempts} restart attempts within {restartWindow.TotalSeconds:0} seconds. Last exit code {exitCode}.";
            }
            else
            {
                delay = TimeSpan.FromMilliseconds(Math.Max(Definition.RestartBackoffMs, 250) * attempt);
                restartAt = now.Add(delay);
                restartPlanVersion = _restartPlanVersion;
                _restartScheduledAt = restartAt;
                _restartSuspended = false;
                _lastNote = $"Exited unexpectedly with code {exitCode}. Restart {attempt}/{allowedAttempts} scheduled in {delay.TotalSeconds:0.#}s.";
            }
        }

        if (recoverySuspended)
        {
            _logger.LogWarning(
                "{Service} crash recovery suspended after {Attempts} attempts within {WindowSeconds} seconds",
                Definition.DisplayName,
                allowedAttempts,
                restartWindow.TotalSeconds);
            return;
        }

        _ = RunScheduledRestartAsync(restartPlanVersion, restartAt);
    }

    private async Task RunScheduledRestartAsync(int restartPlanVersion, DateTimeOffset restartAt)
    {
        try
        {
            var delay = restartAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            lock (_syncRoot)
            {
                if (restartPlanVersion != _restartPlanVersion ||
                    _restartScheduledAt != restartAt ||
                    _restartSuspended ||
                    _stopRequested)
                {
                    return;
                }

                _restartScheduledAt = null;
            }

            await StartCoreAsync(CancellationToken.None, initiatedByRecovery: true, expectedRestartPlanVersion: restartPlanVersion);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to perform crash recovery restart for {Service}", Definition.DisplayName);
        }
    }

    private void MarkHealthyStart(int? port)
    {
        lock (_syncRoot)
        {
            _restartWindowStartedAt = null;
            _restartAttempts = 0;
            _restartSuspended = false;
            _restartScheduledAt = null;
            _hasUnexpectedExit = false;
            _lastExitCode = null;
            _lastNote = port is int actualPort
                ? $"Started and responding on port {actualPort}."
                : $"Started {Definition.DisplayName}.";
        }
    }

    private async Task FinalizeExitedProcessAsync(Process process, Task? stdoutPump, Task? stderrPump)
    {
        try
        {
            if (stdoutPump is not null)
            {
                await stdoutPump;
            }

            if (stderrPump is not null)
            {
                await stderrPump;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Log pumps finished with an exception for {Service}", Definition.DisplayName);
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task FinalizeCancelledStartAsync(Process process, Task stdoutPump, Task stderrPump)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to stop a cancelled restart for {Service}", Definition.DisplayName);
        }

        await FinalizeExitedProcessAsync(process, stdoutPump, stderrPump);
    }

    private async Task PumpStreamAsync(StreamReader reader, string path, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using var writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                System.Text.Encoding.UTF8);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                await writer.WriteLineAsync(line);
                await writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to stream logs for {Service}", Definition.DisplayName);
        }
    }
}
