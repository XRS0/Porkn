using System.Net;
using System.Net.Sockets;

namespace Porkn.Windows;

internal static class PortGuard
{
    public const int DefaultPort = 2080;

    public static int FirstAvailablePort(int start = 2080, int end = 2090)
    {
        for (var port = start; port <= end; port++)
        {
            if (IsAvailable(port)) return port;
        }

        throw new InvalidOperationException($"No free local proxy port in range {start}...{end}");
    }

    private static bool IsAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
