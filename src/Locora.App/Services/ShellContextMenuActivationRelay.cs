using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Locora.App.Services;

public sealed class ShellContextMenuActivationRelay : IDisposable
{
    private readonly string _pipeName;
    private readonly Action<ShellContextMenuCommand> _handleCommand;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    public ShellContextMenuActivationRelay(
        string pipeName,
        Action<ShellContextMenuCommand> handleCommand)
    {
        _pipeName = pipeName;
        _handleCommand = handleCommand;
    }

    public static string CreatePipeName(string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..12];
        return $"Locora.App.Activation.{hash}";
    }

    public static async Task<bool> TrySendAsync(
        string pipeName,
        ShellContextMenuCommand command,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(cancellation.Token);

            var payload = JsonSerializer.Serialize(command);
            await using var writer = new StreamWriter(client, Encoding.UTF8);
            await writer.WriteAsync(payload.AsMemory(), cancellation.Token);
            await writer.FlushAsync(cancellation.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        _serverTask ??= Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cancellation.Token);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(_cancellation.Token);
                var command = JsonSerializer.Deserialize<ShellContextMenuCommand>(payload);
                if (command is not null)
                {
                    _handleCommand(command);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Keep the activation relay alive after malformed payloads or transient pipe failures.
            }
        }
    }
}
