using System.Net.Sockets;

namespace Locora.Supervisor.Services;

public sealed class PortProbe
{
    public async Task<bool> IsPortResponsiveAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(250));

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, timeoutSource.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
