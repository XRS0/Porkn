using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Porkn.Windows;

internal enum ProxyHealthKind
{
    NotChecked,
    Checking,
    Protected,
    ProxyReachable,
    RemoteCheckFailed,
    LocalProxyFailed
}

internal sealed class ProxyHealthStatus
{
    public ProxyHealthKind Kind { get; init; } = ProxyHealthKind.NotChecked;
    public string Title { get; init; } = "Not checked";
    public string Detail { get; init; } = "Connect to verify the local proxy path.";

    public static ProxyHealthStatus NotChecked() => new();
    public static ProxyHealthStatus Checking() => new() { Kind = ProxyHealthKind.Checking, Title = "Checking protection", Detail = "Verifying local proxy and remote path…" };
    public static ProxyHealthStatus Protected(string ip) => new() { Kind = ProxyHealthKind.Protected, Title = "Protected", Detail = $"Proxy is reachable. Remote IP: {ip}" };
    public static ProxyHealthStatus VpnConnected(string entryName) => new() { Kind = ProxyHealthKind.Protected, Title = "VPN connected", Detail = $"Windows RAS VPN entry is connected: {entryName}" };
    public static ProxyHealthStatus ProxyReachable() => new() { Kind = ProxyHealthKind.ProxyReachable, Title = "Proxy reachable", Detail = "Local proxy works, remote IP check returned no IP." };
    public static ProxyHealthStatus RemoteFailed(string message) => new() { Kind = ProxyHealthKind.RemoteCheckFailed, Title = "Remote check failed", Detail = message };
    public static ProxyHealthStatus LocalFailed(string message) => new() { Kind = ProxyHealthKind.LocalProxyFailed, Title = "Local proxy failed", Detail = message };
}

internal sealed class ProxyHealthCheckService
{
    private readonly Uri _ipCheckUri = new("https://api.ipify.org?format=json");

    public async Task<ProxyHealthStatus> CheckAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (!await IsLocalProxyListeningAsync(host, port, cancellationToken))
        {
            return ProxyHealthStatus.LocalFailed($"Local proxy {host}:{port} is not listening.");
        }

        try
        {
            var ip = await FetchProxyIpAsync(host, port, cancellationToken);
            return string.IsNullOrWhiteSpace(ip) ? ProxyHealthStatus.ProxyReachable() : ProxyHealthStatus.Protected(ip);
        }
        catch (Exception ex)
        {
            return ProxyHealthStatus.RemoteFailed(ex.Message);
        }
    }

    private static async Task<bool> IsLocalProxyListeningAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port, cancellationToken).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            if (completed != connectTask) return false;
            await connectTask;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> FetchProxyIpAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(host, port),
            UseProxy = true
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        var body = await http.GetStringAsync(_ipCheckUri, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("ip", out var ip) ? ip.GetString() : body.Trim();
        }
        catch
        {
            return body.Trim();
        }
    }
}
