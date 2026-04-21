using System.Diagnostics;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class PostgreSqlManagedService : ManagedServiceBase
{
    private readonly ILogger<PostgreSqlManagedService> _logger;
    private string? _clusterInitializationFailure;

    public PostgreSqlManagedService(
        ManagedServiceDefinition definition,
        IEnvironmentPaths paths,
        PortProbe portProbe,
        ILogger<PostgreSqlManagedService> logger)
        : base(definition, paths, portProbe, logger)
    {
        _logger = logger;
    }

    public override async Task<ManagedServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await base.GetStatusAsync(cancellationToken);
        var connectionSummary = $"Host 127.0.0.1:{status.Port ?? 5432} / DB postgres / User postgres";

        if (!status.ExecutableExists)
        {
            return status;
        }

        var dataDirectory = GetDataDirectory();
        var clusterInitialized = IsClusterInitialized(dataDirectory);
        var initDbPath = GetInitDbPath();
        var initDbExists = File.Exists(initDbPath);

        if (!clusterInitialized)
        {
            var note = _clusterInitializationFailure;
            if (string.IsNullOrWhiteSpace(note))
            {
                note = initDbExists
                    ? $"Database cluster is not initialized yet. Locora will run initdb on first start. Data directory: {dataDirectory}."
                    : $"Database cluster is not initialized and initdb.exe is missing: {initDbPath}";
            }

            return status with
            {
                State = initDbExists && string.IsNullOrWhiteSpace(_clusterInitializationFailure) ? status.State : "Error",
                Note = AppendSummary(note, connectionSummary)
            };
        }

        return status with { Note = AppendSummary(status.Note, connectionSummary) };
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureClusterInitializedAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    private async Task EnsureClusterInitializedAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = GetDataDirectory();
        if (IsClusterInitialized(dataDirectory))
        {
            _clusterInitializationFailure = null;
            return;
        }

        var initDbPath = GetInitDbPath();
        if (!File.Exists(initDbPath))
        {
            _clusterInitializationFailure = $"initdb.exe is missing at {initDbPath}. Install or unpack PostgreSQL before starting the service.";
            throw new InvalidOperationException(_clusterInitializationFailure);
        }

        Directory.CreateDirectory(dataDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = initDbPath,
                WorkingDirectory = Path.GetDirectoryName(initDbPath) ?? ResolveWorkingDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-D");
        process.StartInfo.ArgumentList.Add(dataDirectory);
        process.StartInfo.ArgumentList.Add("-U");
        process.StartInfo.ArgumentList.Add("postgres");
        process.StartInfo.ArgumentList.Add("-A");
        process.StartInfo.ArgumentList.Add("trust");
        process.StartInfo.ArgumentList.Add("-E");
        process.StartInfo.ArgumentList.Add("UTF8");

        if (!process.Start())
        {
            _clusterInitializationFailure = $"Failed to start initdb for {Definition.DisplayName}.";
            throw new InvalidOperationException(_clusterInitializationFailure);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !IsClusterInitialized(dataDirectory))
        {
            _clusterInitializationFailure = BuildInitDbFailure(process.ExitCode, stderr, stdout, dataDirectory);
            _logger.LogWarning("PostgreSQL initdb failed: {Message}", _clusterInitializationFailure);
            throw new InvalidOperationException(_clusterInitializationFailure);
        }

        _clusterInitializationFailure = null;
        _logger.LogInformation("Initialized PostgreSQL cluster at {DataDirectory}", dataDirectory);
    }

    private string GetDataDirectory()
    {
        var explicitPath = TryGetArgumentValue("-D");
        return string.IsNullOrWhiteSpace(explicitPath)
            ? ResolvePath("data/postgresql/data")
            : ResolvePath(explicitPath);
    }

    private string GetInitDbPath()
    {
        var executablePath = ResolvePath(Definition.RelativeExecutablePath);
        var binDirectory = Path.GetDirectoryName(executablePath) ?? ResolveWorkingDirectory();
        return Path.Combine(binDirectory, "initdb.exe");
    }

    private string? TryGetArgumentValue(string flag)
    {
        for (var index = 0; index < Definition.Arguments.Count - 1; index++)
        {
            if (Definition.Arguments[index].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return ExpandToken(Definition.Arguments[index + 1]);
            }
        }

        return null;
    }

    private static bool IsClusterInitialized(string dataDirectory)
    {
        return File.Exists(Path.Combine(dataDirectory, "PG_VERSION"));
    }

    private static string BuildInitDbFailure(int exitCode, string stderr, string stdout, string dataDirectory)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        detail = string.IsNullOrWhiteSpace(detail) ? "No output captured from initdb." : detail.Trim();
        return $"PostgreSQL cluster initialization failed with exit code {exitCode} for {dataDirectory}. {detail}";
    }

    private static string AppendSummary(string? note, string summary)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return summary;
        }

        return note.Contains(summary, StringComparison.OrdinalIgnoreCase)
            ? note
            : $"{note} {summary}";
    }
}
