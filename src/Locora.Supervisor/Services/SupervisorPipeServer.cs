using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Locora.App.Contracts;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class SupervisorPipeServer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<SupervisorPipeServer> _logger;
    private readonly SupervisorStateStore _stateStore;
    private readonly string _pipeName;

    public SupervisorPipeServer(
        IOptions<AppSettings> appSettings,
        SupervisorStateStore stateStore,
        ILogger<SupervisorPipeServer> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
        _pipeName = appSettings.Value.Supervisor.PipeName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Supervisor pipe server listening on {PipeName}", _pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipeServer = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipeServer.WaitForConnectionAsync(stoppingToken);
            await HandleClientAsync(pipeServer, stoppingToken);
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var request = JsonSerializer.Deserialize<SupervisorRequest>(line, SerializerOptions);
        if (request is null)
        {
            return;
        }

        SupervisorResponse response;

        try
        {
            response = request.Command switch
            {
                SupervisorCommandNames.GetSnapshot => await CreateSnapshotResponseAsync(request.CorrelationId, cancellationToken),
                SupervisorCommandNames.StartAll => await HandleMutationAsync(request.CorrelationId, _stateStore.StartAllAsync, "All configured services processed for start", cancellationToken),
                SupervisorCommandNames.StopAll => await HandleMutationAsync(request.CorrelationId, _stateStore.StopAllAsync, "All configured services processed for stop", cancellationToken),
                SupervisorCommandNames.StartService => await HandleTargetMutationAsync(request, _stateStore.StartServiceAsync, "Service processed for start", cancellationToken),
                SupervisorCommandNames.StopService => await HandleTargetMutationAsync(request, _stateStore.StopServiceAsync, "Service processed for stop", cancellationToken),
                SupervisorCommandNames.StartServicePreset => await HandleTargetMutationAsync(request, _stateStore.StartServicePresetAsync, "Service preset processed for start", cancellationToken),
                SupervisorCommandNames.StopServicePreset => await HandleTargetMutationAsync(request, _stateStore.StopServicePresetAsync, "Service preset processed for stop", cancellationToken),
                SupervisorCommandNames.ApplyHostsPreview => await HandleMutationAsync(request.CorrelationId, _stateStore.ApplyHostsPreviewAsync, "Generated hosts preview applied", cancellationToken),
                SupervisorCommandNames.RollbackHosts => await HandleMutationAsync(request.CorrelationId, _stateStore.RollbackHostsAsync, "Hosts file rolled back", cancellationToken),
                SupervisorCommandNames.RestartElevated => await HandleRestartElevatedAsync(request.CorrelationId, cancellationToken),
                SupervisorCommandNames.ValidateNginxConfig => await HandleMutationAsync(request.CorrelationId, _stateStore.ValidateNginxConfigAsync, "Nginx config validated", cancellationToken),
                SupervisorCommandNames.RepairRuntime => await HandleMutationAsync(request.CorrelationId, _stateStore.RepairRuntimeAsync, "Runtime config repair completed", cancellationToken),
                SupervisorCommandNames.RepairDomains => await HandleMutationAsync(request.CorrelationId, _stateStore.RepairDomainsAsync, "Domain artifact repair completed", cancellationToken),
                SupervisorCommandNames.RepairService => await HandleTargetMutationAsync(request, _stateStore.RepairServiceAsync, "Service repair completed", cancellationToken),
                SupervisorCommandNames.SyncPackageDownloads => await HandleMutationAsync(request.CorrelationId, _stateStore.SyncPackageDownloadsAsync, "Package download sync completed", cancellationToken),
                SupervisorCommandNames.ExtractPackageArchives => await HandleMutationAsync(request.CorrelationId, _stateStore.ExtractPackageArchivesAsync, "Package archive extraction completed", cancellationToken),
                SupervisorCommandNames.InstallOrUpdatePackages => await HandleMutationAsync(request.CorrelationId, _stateStore.InstallOrUpdatePackagesAsync, "Package install or update completed", cancellationToken),
                SupervisorCommandNames.RemovePackageInstall => await HandleTargetMutationAsync(request, _stateStore.RemovePackageInstallAsync, "Package install removed", cancellationToken),
                SupervisorCommandNames.SelectRuntimeVersion => await HandleTargetMutationAsync(request, _stateStore.SelectRuntimeVersionAsync, "Runtime version selected", cancellationToken),
                SupervisorCommandNames.SelectStackProfile => await HandleTargetMutationAsync(request, _stateStore.SelectStackProfileAsync, "Stack profile selected", cancellationToken),
                SupervisorCommandNames.SaveActiveEnvironment => await HandleTargetMutationAsync(request, _stateStore.SaveActiveEnvironmentAsync, "Active environment saved as stack profile", cancellationToken),
                SupervisorCommandNames.ExportStackProfiles => await HandleTargetMutationAsync(request, _stateStore.ExportStackProfilesAsync, "Stack profiles exported", cancellationToken),
                SupervisorCommandNames.ImportStackProfiles => await HandleTargetMutationAsync(request, _stateStore.ImportStackProfilesAsync, "Stack profiles imported", cancellationToken),
                SupervisorCommandNames.RepairLocalSsl => await HandleMutationAsync(request.CorrelationId, _stateStore.RepairLocalSslAsync, "Local SSL repaired", cancellationToken),
                SupervisorCommandNames.GenerateLocalSsl => await HandleMutationAsync(request.CorrelationId, _stateStore.GenerateLocalSslAsync, "Local SSL generated", cancellationToken),
                SupervisorCommandNames.TrustLocalSslCa => await HandleMutationAsync(request.CorrelationId, _stateStore.TrustLocalSslCaAsync, "Local SSL CA trusted", cancellationToken),
                SupervisorCommandNames.RollbackLocalSslTrust => await HandleMutationAsync(request.CorrelationId, _stateStore.RollbackLocalSslTrustAsync, "Local SSL CA trust removed", cancellationToken),
                _ => new SupervisorResponse(request.CorrelationId, false, $"Unknown command '{request.Command}'", null)
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supervisor command {Command} failed", request.Command);
            response = new SupervisorResponse(request.CorrelationId, false, exception.Message, null);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions));
    }

    private async Task<SupervisorResponse> CreateSnapshotResponseAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var snapshot = await _stateStore.GetSnapshotAsync(cancellationToken);
        return new SupervisorResponse(correlationId, true, "Snapshot ready", snapshot);
    }

    private async Task<SupervisorResponse> HandleMutationAsync(
        Guid correlationId,
        Func<CancellationToken, Task> operation,
        string message,
        CancellationToken cancellationToken)
    {
        await operation(cancellationToken);
        _logger.LogInformation("{Message}", message);
        var snapshot = await _stateStore.GetSnapshotAsync(cancellationToken);
        return new SupervisorResponse(correlationId, true, message, snapshot);
    }

    private async Task<SupervisorResponse> HandleTargetMutationAsync(
        SupervisorRequest request,
        Func<string, CancellationToken, Task> operation,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetKey))
        {
            return new SupervisorResponse(request.CorrelationId, false, "Missing target key.", null);
        }

        await operation(request.TargetKey, cancellationToken);
        _logger.LogInformation("{Message}: {ServiceKey}", message, request.TargetKey);
        var snapshot = await _stateStore.GetSnapshotAsync(cancellationToken);
        return new SupervisorResponse(request.CorrelationId, true, message, snapshot);
    }

    private async Task<SupervisorResponse> HandleRestartElevatedAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        await _stateStore.RestartElevatedAsync(cancellationToken);
        return new SupervisorResponse(
            correlationId,
            true,
            "Elevated supervisor restart requested. Approve the Windows UAC prompt, then refresh after the new supervisor starts.",
            null);
    }
}
