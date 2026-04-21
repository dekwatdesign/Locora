using System.Security.Cryptography.X509Certificates;
using Locora.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Locora.Supervisor.Services;

public sealed class PermissionDiagnosticsService
{
    private readonly IEnvironmentPaths _paths;
    private readonly HostsFileManager _hostsFileManager;
    private readonly SupervisorPrivilegeService _privilegeService;
    private readonly ILogger<PermissionDiagnosticsService> _logger;

    public PermissionDiagnosticsService(
        IEnvironmentPaths paths,
        HostsFileManager hostsFileManager,
        SupervisorPrivilegeService privilegeService,
        ILogger<PermissionDiagnosticsService> logger)
    {
        _paths = paths;
        _hostsFileManager = hostsFileManager;
        _privilegeService = privilegeService;
        _logger = logger;
    }

    public IReadOnlyList<PermissionDiagnosticSnapshot> GetDiagnostics()
    {
        return
        [
            CheckPortableWorkspaceAccess(),
            CheckHostsWritePath(),
            CheckElevationRestartAvailability(),
            CheckCurrentUserSslTrustAccess()
        ];
    }

    private PermissionDiagnosticSnapshot CheckPortableWorkspaceAccess()
    {
        var probeTargets = new[]
        {
            ("config", _paths.ConfigRoot),
            ("logs", _paths.LogsRoot),
            ("temp", _paths.TempRoot)
        };

        var failures = new List<string>();

        foreach (var (label, path) in probeTargets)
        {
            if (!TryProbeDirectoryWriteAccess(path, out var failureReason))
            {
                failures.Add($"{label}: {path} ({failureReason})");
            }
        }

        if (failures.Count == 0)
        {
            return new PermissionDiagnosticSnapshot(
                "portable_workspace_access",
                "Portable workspace write access",
                "Ready",
                "Locora can write to the config, logs, and temp roots under the portable workspace.",
                $"Checked {_paths.ConfigRoot}, {_paths.LogsRoot}, and {_paths.TempRoot}.",
                "No action needed.");
        }

        return new PermissionDiagnosticSnapshot(
            "portable_workspace_access",
            "Portable workspace write access",
            "Blocked",
            "One or more required Locora workspace folders are not writable.",
            string.Join(" ", failures),
            "Move the portable root to a writable location or adjust filesystem permissions before retrying config generation, logging, or package setup.");
    }

    private PermissionDiagnosticSnapshot CheckHostsWritePath()
    {
        var hostsPath = _hostsFileManager.HostsFilePath;
        var overridePath = Environment.GetEnvironmentVariable("LOCORA_HOSTS_FILE");

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var hostsDirectory = Path.GetDirectoryName(hostsPath);
            if (string.IsNullOrWhiteSpace(hostsDirectory))
            {
                return new PermissionDiagnosticSnapshot(
                    "hosts_write_path",
                    "Hosts file update path",
                    "Blocked",
                    "LOCORA_HOSTS_FILE is set, but the override path does not resolve to a writable directory.",
                    $"Resolved path: {hostsPath}",
                    "Update LOCORA_HOSTS_FILE to point at a normal writable test file, then refresh the dashboard.");
            }

            if (!TryProbeDirectoryWriteAccess(hostsDirectory, out var failureReason))
            {
                return new PermissionDiagnosticSnapshot(
                    "hosts_write_path",
                    "Hosts file update path",
                    "Blocked",
                    "The hosts override target is configured, but Locora cannot write beside that file.",
                    $"{hostsPath} ({failureReason})",
                    "Choose a writable override path or remove LOCORA_HOSTS_FILE to fall back to the real Windows hosts file.");
            }

            return new PermissionDiagnosticSnapshot(
                "hosts_write_path",
                "Hosts file update path",
                "Ready",
                "Hosts updates are redirected to the LOCORA_HOSTS_FILE override, so elevation is not required for preview testing.",
                $"Override target: {hostsPath}",
                "Use Apply Preview safely against the override file, or clear LOCORA_HOSTS_FILE when you are ready to write the real Windows hosts file.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return new PermissionDiagnosticSnapshot(
                "hosts_write_path",
                "Hosts file update path",
                "Blocked",
                "Windows hosts automation is only supported on Windows.",
                $"Resolved target: {hostsPath}",
                "Run Locora on Windows or set LOCORA_HOSTS_FILE to a safe override file while developing outside the target runtime.");
        }

        if (_privilegeService.IsElevated())
        {
            return new PermissionDiagnosticSnapshot(
                "hosts_write_path",
                "Hosts file update path",
                "Ready",
                "The supervisor is elevated and can update the real Windows hosts file managed block.",
                $"Windows hosts path: {hostsPath}",
                "Apply Preview can write directly and create rollback backups.");
        }

        return new PermissionDiagnosticSnapshot(
            "hosts_write_path",
            "Hosts file update path",
            "Attention",
            "Applying the real Windows hosts file usually requires administrator rights.",
            $"Windows hosts path: {hostsPath}",
            "Use Restart Supervisor as Admin before applying the preview, or set LOCORA_HOSTS_FILE to a writable override path for safe testing.");
    }

    private PermissionDiagnosticSnapshot CheckElevationRestartAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PermissionDiagnosticSnapshot(
                "elevation_restart",
                "Restart-as-admin flow",
                "Blocked",
                "The UAC restart flow is only available on Windows.",
                "Locora relies on the Windows runas verb to relaunch the supervisor with administrator rights.",
                "Run Locora on Windows when you need protected hosts or shell integration operations.");
        }

        if (_privilegeService.IsElevated())
        {
            return new PermissionDiagnosticSnapshot(
                "elevation_restart",
                "Restart-as-admin flow",
                "Ready",
                "The supervisor is already running as administrator.",
                $"Current executable: {Environment.ProcessPath ?? "Unavailable"}",
                "No action needed unless you intentionally want to relaunch the supervisor.");
        }

        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return new PermissionDiagnosticSnapshot(
                "elevation_restart",
                "Restart-as-admin flow",
                "Blocked",
                "Locora could not resolve the current supervisor executable path for a UAC restart.",
                "Environment.ProcessPath was empty.",
                "Launch Locora.Supervisor from a normal executable path, then retry the restart-as-admin action.");
        }

        return new PermissionDiagnosticSnapshot(
            "elevation_restart",
            "Restart-as-admin flow",
            "Ready",
            "Locora can request a UAC relaunch for the supervisor when protected operations need administrator rights.",
            $"Current executable: {Environment.ProcessPath}",
            "Use Restart Supervisor as Admin before editing the real Windows hosts file or other protected OS locations.");
    }

    private PermissionDiagnosticSnapshot CheckCurrentUserSslTrustAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PermissionDiagnosticSnapshot(
                "current_user_ssl_trust",
                "Current-user SSL trust access",
                "Blocked",
                "Current-user certificate trust diagnostics are only available on Windows.",
                "Locora uses the Windows CurrentUser root certificate store for local CA trust.",
                "Run Locora on Windows to trust or untrust the Locora development certificate authority.");
        }

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Close();

            return new PermissionDiagnosticSnapshot(
                "current_user_ssl_trust",
                "Current-user SSL trust access",
                "Ready",
                "Locora can manage CurrentUser certificate trust without elevating the full supervisor.",
                "The CurrentUser root certificate store accepted a read-write open.",
                "You can use Trust Local CA or Remove Local CA Trust directly from Domains & Hosts.");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Current-user SSL trust diagnostics failed.");

            return new PermissionDiagnosticSnapshot(
                "current_user_ssl_trust",
                "Current-user SSL trust access",
                "Blocked",
                "Locora could not open the CurrentUser root certificate store for local CA trust changes.",
                exception.Message,
                "Use a Windows account with certificate-store access, then retry Trust Local CA.");
        }
    }

    private bool TryProbeDirectoryWriteAccess(string directoryPath, out string failureReason)
    {
        var probePath = Path.Combine(directoryPath, $".locora-permission-check-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directoryPath);

            using (var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("locora");
            }

            File.Delete(probePath);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Write probe failed for {Path}", directoryPath);
            failureReason = exception.Message;

            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }

            return false;
        }
    }
}

public sealed record PermissionDiagnosticSnapshot(
    string Key,
    string DisplayName,
    string State,
    string Summary,
    string Details,
    string SuggestedAction);
