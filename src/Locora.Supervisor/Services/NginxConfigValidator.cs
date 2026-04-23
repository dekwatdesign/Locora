using System.Diagnostics;
using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class NginxConfigValidator
{
    private readonly IEnvironmentPaths _paths;
    private readonly ManagedServicesOptions _options;
    private ConfigValidationResult? _lastResult;

    public NginxConfigValidator(
        IEnvironmentPaths paths,
        IOptions<ManagedServicesOptions> options)
    {
        _paths = paths;
        _options = options.Value;
    }

    public ConfigValidationResult? LastResult => _lastResult;

    public async Task<ConfigValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var definition = _options.Services.FirstOrDefault(service =>
            service.Kind.Equals("nginx", StringComparison.OrdinalIgnoreCase) ||
            service.Key.Equals("nginx", StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return Store(new ConfigValidationResult(
                "nginx",
                "Nginx",
                false,
                "Nginx is not configured",
                "No Nginx service definition exists in usr/config/services.json.",
                DateTimeOffset.UtcNow));
        }

        var executablePath = ResolvePath(definition.RelativeExecutablePath);
        var configPath = Path.Combine(_paths.ConfigRoot, "nginx", "nginx.conf");

        if (!File.Exists(executablePath))
        {
            return Store(new ConfigValidationResult(
                definition.Key,
                definition.DisplayName,
                false,
                "Nginx executable is missing",
                string.IsNullOrWhiteSpace(definition.VersionResolutionNote)
                    ? $"Expected executable: {executablePath}"
                    : $"{definition.VersionResolutionNote} Expected executable: {executablePath}",
                DateTimeOffset.UtcNow));
        }

        if (!File.Exists(configPath))
        {
            return Store(new ConfigValidationResult(
                definition.Key,
                definition.DisplayName,
                false,
                "Nginx config has not been generated",
                $"Expected config: {configPath}",
                DateTimeOffset.UtcNow));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = ResolveWorkingDirectory(definition),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Nginx config validation process.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            var timeoutDetails = BuildDetails(await stdoutTask, await stderrTask);
            return Store(new ConfigValidationResult(
                definition.Key,
                definition.DisplayName,
                false,
                "Nginx config validation timed out",
                string.IsNullOrWhiteSpace(timeoutDetails)
                    ? "The validation process did not finish within 10 seconds."
                    : timeoutDetails,
                DateTimeOffset.UtcNow));
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var details = BuildDetails(stdout, stderr);

        return Store(new ConfigValidationResult(
            definition.Key,
            definition.DisplayName,
            process.ExitCode == 0,
            process.ExitCode == 0 ? "Nginx config is valid" : $"Nginx config validation failed with exit code {process.ExitCode}",
            string.IsNullOrWhiteSpace(details) ? "No validation output was produced." : details,
            DateTimeOffset.UtcNow));
    }

    private static string BuildDetails(params string[] values)
    {
        return string.Join(
                Environment.NewLine,
                values.Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
    }

    private ConfigValidationResult Store(ConfigValidationResult result)
    {
        _lastResult = result;
        return result;
    }

    private string ResolvePath(string value)
    {
        var expanded = ExpandToken(value);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, expanded));
    }

    private string ResolveWorkingDirectory(ManagedServiceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.RelativeWorkingDirectory))
        {
            return Path.GetDirectoryName(ResolvePath(definition.RelativeExecutablePath)) ?? _paths.AppRoot;
        }

        return ResolvePath(definition.RelativeWorkingDirectory);
    }

    private string ExpandToken(string value)
    {
        return value
            .Replace("{root}", _paths.AppRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{bin}", _paths.BinRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{config}", _paths.ConfigRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{logs}", _paths.LogsRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{data}", _paths.DataRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{temp}", _paths.TempRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{projects}", _paths.ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }
}
