using System.Diagnostics;
using System.Net.Sockets;

namespace Porkn.Windows;

internal sealed class PingService
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    public async Task<int?> MeasureAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        if (profile.Port <= 0) return null;
        return await MeasureAsync(profile.Host, profile.Port, cancellationToken);
    }

    public async Task<int?> MeasureAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            var connectTask = client.ConnectAsync(host, port, cancellationToken).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(Timeout, cancellationToken));
            if (completed != connectTask) return null;
            await connectTask;
            stopwatch.Stop();
            return Math.Max(1, (int)stopwatch.ElapsedMilliseconds);
        }
        catch
        {
            return null;
        }
    }
}
