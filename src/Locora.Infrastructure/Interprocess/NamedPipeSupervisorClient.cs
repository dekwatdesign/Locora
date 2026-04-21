using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Locora.App.Contracts;
using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Infrastructure.Interprocess;

public sealed class NamedPipeSupervisorClient : ISupervisorClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<NamedPipeSupervisorClient> _logger;

    public NamedPipeSupervisorClient(
        IOptions<AppSettings> settings,
        ILogger<NamedPipeSupervisorClient> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<EnvironmentSnapshotDto> GetEnvironmentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(SupervisorCommandNames.GetSnapshot, cancellationToken);
        return response.Snapshot ?? throw new InvalidOperationException("Supervisor did not return a snapshot.");
    }

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.StartAll, cancellationToken);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.StopAll, cancellationToken);
    }

    public async Task StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.StartService, cancellationToken, serviceKey);
    }

    public async Task StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.StopService, cancellationToken, serviceKey);
    }

    public async Task ApplyHostsPreviewAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.ApplyHostsPreview, cancellationToken);
    }

    public async Task RollbackHostsAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RollbackHosts, cancellationToken);
    }

    public async Task RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RestartElevated, cancellationToken);
    }

    public async Task ValidateNginxConfigAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.ValidateNginxConfig, cancellationToken);
    }

    public async Task RepairRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RepairRuntime, cancellationToken);
    }

    public async Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RepairService, cancellationToken, serviceKey);
    }

    public async Task RepairLocalSslAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RepairLocalSsl, cancellationToken);
    }

    public async Task GenerateLocalSslAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.GenerateLocalSsl, cancellationToken);
    }

    public async Task TrustLocalSslCaAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.TrustLocalSslCa, cancellationToken);
    }

    public async Task RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(SupervisorCommandNames.RollbackLocalSslTrust, cancellationToken);
    }

    private async Task<SupervisorResponse> SendAsync(string command, CancellationToken cancellationToken, string? targetKey = null)
    {
        var pipeName = _settings.Value.Supervisor.PipeName;
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(_settings.Value.Supervisor.ConnectTimeoutMs, cancellationToken);

        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        var correlationId = Guid.NewGuid();
        var request = new SupervisorRequest(correlationId, command, targetKey);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions));
        var line = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException("Supervisor returned an empty response.");
        }

        var response = JsonSerializer.Deserialize<SupervisorResponse>(line, SerializerOptions)
            ?? throw new InvalidOperationException("Supervisor response could not be deserialized.");

        if (!response.Success)
        {
            _logger.LogWarning("Supervisor responded with a failure for command {Command}: {Message}", command, response.Message);
            throw new InvalidOperationException(response.Message ?? $"Supervisor command '{command}' failed.");
        }

        return response;
    }
}
