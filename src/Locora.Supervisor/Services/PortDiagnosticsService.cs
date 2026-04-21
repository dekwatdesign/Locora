using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Locora.Supervisor.Services;

public sealed class PortDiagnosticsService
{
    public IReadOnlyList<PortDiagnosticSnapshot> GetDiagnostics(IReadOnlyList<ManagedServiceStatus> statuses)
    {
        var configuredStatuses = statuses
            .Where(status => status.Port is not null)
            .OrderBy(status => status.Port)
            .ThenBy(status => status.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configuredStatuses.Count == 0)
        {
            return [];
        }

        var activePorts = GetActiveTcpListenerPorts();
        var portOwners = GetPortOwners();
        var duplicatePorts = configuredStatuses
            .GroupBy(status => status.Port!.Value)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.ToList());

        return configuredStatuses
            .Select(status => CreateDiagnostic(status, activePorts, portOwners, duplicatePorts))
            .ToList();
    }

    private static PortDiagnosticSnapshot CreateDiagnostic(
        ManagedServiceStatus status,
        ISet<int> activePorts,
        IReadOnlyDictionary<int, PortOwnerSnapshot> portOwners,
        IReadOnlyDictionary<int, List<ManagedServiceStatus>> duplicatePorts)
    {
        var port = status.Port!.Value;
        var isPortListening = status.PortResponsive || activePorts.Contains(port);
        portOwners.TryGetValue(port, out var owner);

        if (duplicatePorts.TryGetValue(port, out var duplicateServices))
        {
            var serviceNames = string.Join(", ", duplicateServices.Select(service => service.DisplayName));
            var ownerDetails = owner is null ? "No active listener owner was identified." : $"Current listener: {FormatOwner(owner)}.";

            return new PortDiagnosticSnapshot(
                $"port_duplicate_{port}_{status.Key}",
                $"{status.DisplayName} port {port}",
                port,
                "Blocked",
                $"Port {port} is configured by multiple Locora services.",
                $"{serviceNames} all target port {port}. {ownerDetails}",
                "Change one service port in usr/config/services.json, then repair runtime configs and refresh diagnostics.",
                owner?.ProcessId,
                owner?.ProcessName);
        }

        if (status.ProcessId is int trackedProcessId &&
            isPortListening &&
            owner is not null &&
            owner.ProcessId != trackedProcessId)
        {
            return new PortDiagnosticSnapshot(
                $"port_owner_mismatch_{status.Key}_{port}",
                $"{status.DisplayName} port {port}",
                port,
                "Blocked",
                $"Port {port} is owned by {FormatOwner(owner)}, not the tracked {status.DisplayName} process.",
                $"Locora is tracking PID {trackedProcessId}, but the active TCP listener belongs to {FormatOwner(owner)}.",
                "Stop the conflicting listener, then restart the Locora-managed service so the tracked process can bind the port.",
                owner.ProcessId,
                owner.ProcessName);
        }

        if (status.ProcessId is int processId)
        {
            if (status.PortResponsive)
            {
                return new PortDiagnosticSnapshot(
                    $"port_{status.Key}_{port}",
                    $"{status.DisplayName} port {port}",
                    port,
                    "Ready",
                    $"{status.DisplayName} is responding on port {port}.",
                    $"Locora is tracking PID {processId} for {status.DisplayName}.",
                    "No action needed.",
                    processId,
                    status.DisplayName);
            }

            return new PortDiagnosticSnapshot(
                $"port_{status.Key}_{port}",
                $"{status.DisplayName} port {port}",
                port,
                "Attention",
                $"{status.DisplayName} is running but port {port} is not responding yet.",
                status.Note ?? "The process is tracked by Locora, but the configured TCP port did not accept connections.",
                "Inspect service stdout/stderr logs, validate generated configs, and retry after the service finishes starting.",
                processId,
                status.DisplayName);
        }

        if (isPortListening)
        {
            var ownerLabel = owner is null ? "another process" : FormatOwner(owner);

            return new PortDiagnosticSnapshot(
                $"port_external_{status.Key}_{port}",
                $"{status.DisplayName} port {port}",
                port,
                "Blocked",
                $"Port {port} is already in use by {ownerLabel}.",
                $"{status.DisplayName} is not tracked as the owner of port {port}. Starting it may fail or Locora may mistake the external listener for a healthy service.",
                "Stop the external listener, change the Locora service port, or stop the conflicting system service before starting this service.",
                owner?.ProcessId,
                owner?.ProcessName);
        }

        return new PortDiagnosticSnapshot(
            $"port_{status.Key}_{port}",
            $"{status.DisplayName} port {port}",
            port,
            "Ready",
            $"Port {port} is available for {status.DisplayName}.",
            "No active TCP listener was detected on the configured port.",
            "No action needed.",
            null,
            null);
    }

    private static HashSet<int> GetActiveTcpListenerPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<int, PortOwnerSnapshot> GetPortOwners()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<int, PortOwnerSnapshot>();
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("-ano");
            process.StartInfo.ArgumentList.Add("-p");
            process.StartInfo.ArgumentList.Add("tcp");

            if (!process.Start())
            {
                return new Dictionary<int, PortOwnerSnapshot>();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(milliseconds: 1500))
            {
                process.Kill(entireProcessTree: true);
                return new Dictionary<int, PortOwnerSnapshot>();
            }

            return ParseNetstatOutput(outputTask.GetAwaiter().GetResult());
        }
        catch
        {
            return new Dictionary<int, PortOwnerSnapshot>();
        }
    }

    private static Dictionary<int, PortOwnerSnapshot> ParseNetstatOutput(string output)
    {
        var owners = new Dictionary<int, PortOwnerSnapshot>();

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 ||
                !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) ||
                !parts[^2].Equals("LISTENING", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(parts[^1], out var processId) ||
                !TryExtractPort(parts[1], out var port))
            {
                continue;
            }

            owners.TryAdd(port, CreateOwner(processId));
        }

        return owners;
    }

    private static bool TryExtractPort(string endpoint, out int port)
    {
        var separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == endpoint.Length - 1)
        {
            port = 0;
            return false;
        }

        var portText = endpoint[(separatorIndex + 1)..].TrimEnd(']');
        return int.TryParse(portText, out port);
    }

    private static PortOwnerSnapshot CreateOwner(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new PortOwnerSnapshot(processId, process.ProcessName);
        }
        catch
        {
            return new PortOwnerSnapshot(processId, "unknown");
        }
    }

    private static string FormatOwner(PortOwnerSnapshot owner)
    {
        return string.IsNullOrWhiteSpace(owner.ProcessName)
            ? $"PID {owner.ProcessId}"
            : $"{owner.ProcessName} (PID {owner.ProcessId})";
    }

    private sealed record PortOwnerSnapshot(int ProcessId, string ProcessName);
}
