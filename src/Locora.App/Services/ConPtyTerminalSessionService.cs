using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Locora.App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Win32.SafeHandles;

namespace Locora.App.Services;

public sealed class ConPtyTerminalSessionService : ITerminalSessionService, IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private static readonly IntPtr ProcThreadAttributePseudoConsole = 0x00020016;

    private readonly object _gate = new();
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly ILogger<ConPtyTerminalSessionService> _logger;
    private readonly Dictionary<string, RunningTerminalSession> _runningSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _pendingInputs = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ConPtyTerminalSessionService(ILogger<ConPtyTerminalSessionService> logger)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _logger = logger;
    }

    public ObservableCollection<TerminalSession> Sessions { get; } = new();

    public TerminalSession StartSession(string title, ProjectTerminalLaunchProfile launchProfile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(launchProfile.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"Terminal working directory does not exist: {launchProfile.WorkingDirectory}");
        }

        var backend = CanUseConPty() ? "ConPTY" : "Process";
        var session = new TerminalSession(
            Guid.NewGuid().ToString("N"),
            title,
            launchProfile,
            backend,
            DateTimeOffset.Now);

        session.AppendOutput($"Locora terminal session started in {launchProfile.WorkingDirectory}{Environment.NewLine}");
        Sessions.Add(session);

        _ = Task.Run(() => RunSessionAsync(session, launchProfile));
        return session;
    }

    public void SendInput(TerminalSession session, string input)
    {
        if (session is null || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var line = input.EndsWith('\n')
            ? input
            : input + Environment.NewLine;
        RunningTerminalSession? runningSession;
        lock (_gate)
        {
            if (!_runningSessions.TryGetValue(session.Id, out runningSession))
            {
                if (!_pendingInputs.TryGetValue(session.Id, out var bufferedInputs))
                {
                    bufferedInputs = new List<string>();
                    _pendingInputs[session.Id] = bufferedInputs;
                }

                bufferedInputs.Add(line);
                return;
            }
        }

        _ = Task.Run(() => runningSession.WriteInputAsync(line));
    }

    public void StopSession(TerminalSession session)
    {
        if (session is null)
        {
            return;
        }

        RunningTerminalSession? runningSession;
        lock (_gate)
        {
            _runningSessions.TryGetValue(session.Id, out runningSession);
        }

        runningSession?.Stop();
    }

    public void CloseSession(TerminalSession session)
    {
        if (session is null)
        {
            return;
        }

        RunningTerminalSession? runningSession;
        lock (_gate)
        {
            _runningSessions.Remove(session.Id, out runningSession);
            _pendingInputs.Remove(session.Id);
        }

        runningSession?.Dispose();
        Sessions.Remove(session);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<RunningTerminalSession> runningSessions;
        lock (_gate)
        {
            runningSessions = _runningSessions.Values.ToList();
            _runningSessions.Clear();
            _pendingInputs.Clear();
        }

        foreach (var session in runningSessions)
        {
            session.Dispose();
        }
    }

    private async Task RunSessionAsync(TerminalSession session, ProjectTerminalLaunchProfile launchProfile)
    {
        try
        {
            Dispatch(() => session.MarkStarted());

            if (CanUseConPty())
            {
                await RunConPtySessionAsync(session, launchProfile).ConfigureAwait(false);
            }
            else
            {
                await RunRedirectedProcessSessionAsync(session, launchProfile).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Built-in terminal session failed for {Title}.", session.Title);
            Dispatch(() =>
            {
                session.AppendOutput($"{Environment.NewLine}Terminal failed: {exception.Message}{Environment.NewLine}");
                session.MarkExited(null);
            });
        }
        finally
        {
            lock (_gate)
            {
                _runningSessions.Remove(session.Id);
                _pendingInputs.Remove(session.Id);
            }
        }
    }

    private async Task RunConPtySessionAsync(TerminalSession session, ProjectTerminalLaunchProfile launchProfile)
    {
        if (!CreatePipe(out var pseudoConsoleInputRead, out var inputWriter, IntPtr.Zero, 0) ||
            !CreatePipe(out var outputReader, out var pseudoConsoleOutputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create ConPTY pipes.");
        }

        var pseudoConsoleHandle = IntPtr.Zero;
        var attributeList = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;
        Process? process = null;
        FileStream? inputStream = null;
        FileStream? outputStream = null;
        CancellationTokenSource? outputCancellation = null;
        RunningTerminalSession? runningSession = null;

        try
        {
            var hr = CreatePseudoConsole(new Coord(120, 32), pseudoConsoleInputRead, pseudoConsoleOutputWrite, 0, out pseudoConsoleHandle);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            pseudoConsoleInputRead.Dispose();
            pseudoConsoleOutputWrite.Dispose();

            attributeList = CreatePseudoConsoleAttributeList(pseudoConsoleHandle);
            environmentBlock = CreateEnvironmentBlock(launchProfile);

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfoEx>()
                },
                lpAttributeList = attributeList
            };

            var commandLine = new StringBuilder(QuoteCommandLineArgument(ResolveShellPath()));
            if (!CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                environmentBlock,
                launchProfile.WorkingDirectory,
                ref startupInfo,
                out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create ConPTY child process.");
            }

            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);

            process = Process.GetProcessById(processInformation.dwProcessId);
            inputStream = new FileStream(inputWriter, FileAccess.Write, 4096, isAsync: true);
            outputStream = new FileStream(outputReader, FileAccess.Read, 4096, isAsync: true);
            outputCancellation = new CancellationTokenSource();
            runningSession = new RunningTerminalSession(process, inputStream, null, outputCancellation);
            RegisterRunningSession(session, runningSession);

            var outputTask = ReadOutputAsync(session, outputStream, outputCancellation.Token);
            await process.WaitForExitAsync().ConfigureAwait(false);
            outputCancellation.Cancel();
            await ObserveOutputCompletionAsync(outputTask).ConfigureAwait(false);

            Dispatch(() => session.MarkExited(process.ExitCode));
        }
        finally
        {
            runningSession?.DisposeStreamsOnly();
            outputCancellation?.Dispose();
            process?.Dispose();

            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }

            if (pseudoConsoleHandle != IntPtr.Zero)
            {
                ClosePseudoConsole(pseudoConsoleHandle);
            }

            if (!pseudoConsoleInputRead.IsInvalid)
            {
                pseudoConsoleInputRead.Dispose();
            }

            if (!pseudoConsoleOutputWrite.IsInvalid)
            {
                pseudoConsoleOutputWrite.Dispose();
            }
        }
    }

    private async Task RunRedirectedProcessSessionAsync(TerminalSession session, ProjectTerminalLaunchProfile launchProfile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveShellPath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = launchProfile.WorkingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        TerminalLaunchEnvironment.Apply(startInfo, launchProfile);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start terminal process '{startInfo.FileName}'.");
        }

        using var outputCancellation = new CancellationTokenSource();
        var runningSession = new RunningTerminalSession(process, null, process.StandardInput, outputCancellation);
        RegisterRunningSession(session, runningSession);

        var standardOutputTask = ReadReaderAsync(session, process.StandardOutput, outputCancellation.Token);
        var standardErrorTask = ReadReaderAsync(session, process.StandardError, outputCancellation.Token);

        await process.WaitForExitAsync().ConfigureAwait(false);
        outputCancellation.Cancel();
        await ObserveOutputCompletionAsync(Task.WhenAll(standardOutputTask, standardErrorTask)).ConfigureAwait(false);

        Dispatch(() => session.MarkExited(process.ExitCode));
    }

    private void RegisterRunningSession(TerminalSession session, RunningTerminalSession runningSession)
    {
        List<string>? bufferedInputs = null;
        lock (_gate)
        {
            _runningSessions[session.Id] = runningSession;
            if (_pendingInputs.Remove(session.Id, out var pendingInputs))
            {
                bufferedInputs = pendingInputs;
            }
        }

        if (bufferedInputs is null || bufferedInputs.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var input in bufferedInputs)
            {
                await runningSession.WriteInputAsync(input).ConfigureAwait(false);
            }
        });
    }

    private async Task ReadOutputAsync(TerminalSession session, Stream outputStream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await outputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Dispatch(() => session.AppendOutput(text));
        }
    }

    private async Task ReadReaderAsync(TerminalSession session, TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var charactersRead = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (charactersRead <= 0)
            {
                break;
            }

            Dispatch(() => session.AppendOutput(new string(buffer, 0, charactersRead)));
        }
    }

    private static async Task ObserveOutputCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Dispatch(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private static bool CanUseConPty()
    {
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
    }

    private static string ResolveShellPath()
    {
        var comSpec = Environment.GetEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comSpec))
        {
            return comSpec;
        }

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        return string.IsNullOrWhiteSpace(systemRoot)
            ? "cmd.exe"
            : Path.Combine(systemRoot, "System32", "cmd.exe");
    }

    private static string QuoteCommandLineArgument(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "\"cmd.exe\""
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static IntPtr CreateEnvironmentBlock(ProjectTerminalLaunchProfile launchProfile)
    {
        var environment = TerminalLaunchEnvironment.Build(launchProfile);
        var builder = new StringBuilder();

        foreach (var pair in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Contains('='))
            {
                continue;
            }

            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private static IntPtr CreatePseudoConsoleAttributeList(IntPtr pseudoConsoleHandle)
    {
        var attributeListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

        var attributeList = Marshal.AllocHGlobal(attributeListSize);
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process thread attribute list.");
        }

        if (!UpdateProcThreadAttribute(
            attributeList,
            0,
            ProcThreadAttributePseudoConsole,
            pseudoConsoleHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to bind ConPTY to child process startup info.");
        }

        return attributeList;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }

        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private sealed class RunningTerminalSession : IDisposable
    {
        private readonly Process _process;
        private readonly Stream? _inputStream;
        private readonly TextWriter? _inputWriter;
        private readonly CancellationTokenSource _outputCancellation;
        private bool _disposed;

        public RunningTerminalSession(
            Process process,
            Stream? inputStream,
            TextWriter? inputWriter,
            CancellationTokenSource outputCancellation)
        {
            _process = process;
            _inputStream = inputStream;
            _inputWriter = inputWriter;
            _outputCancellation = outputCancellation;
        }

        public async Task WriteInputAsync(string input)
        {
            if (_disposed)
            {
                return;
            }

            if (_inputStream is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                await _inputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await _inputStream.FlushAsync().ConfigureAwait(false);
                return;
            }

            if (_inputWriter is not null)
            {
                await _inputWriter.WriteAsync(input).ConfigureAwait(false);
                await _inputWriter.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Stop()
        {
            if (_disposed)
            {
                return;
            }

            _outputCancellation.Cancel();

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        public void DisposeStreamsOnly()
        {
            _inputStream?.Dispose();
            _inputWriter?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
            DisposeStreamsOnly();
        }
    }
}
