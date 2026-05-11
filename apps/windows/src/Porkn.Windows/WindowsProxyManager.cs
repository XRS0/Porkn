using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Porkn.Windows;

internal sealed class WindowsProxyManager
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private readonly string _snapshotPath = Path.Combine(AppPaths.RuntimeDirectory, "windows-proxy-snapshot.json");

    public void Enable(string host, int port)
    {
        Directory.CreateDirectory(AppPaths.RuntimeDirectory);
        if (!File.Exists(_snapshotPath))
        {
            File.WriteAllText(_snapshotPath, JsonSerializer.Serialize(ReadCurrent(), new JsonSerializerOptions { WriteIndented = true }));
        }

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open Windows Internet Settings registry key");
        var endpoint = $"{host}:{port}";
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"http={endpoint};https={endpoint};socks={endpoint}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
        NotifyProxyChanged();
    }

    public void Restore()
    {
        if (!File.Exists(_snapshotPath))
        {
            DisableIfManaged();
            return;
        }

        var snapshot = JsonSerializer.Deserialize<ProxySnapshot>(File.ReadAllText(_snapshotPath));
        if (snapshot is null) return;

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open Windows Internet Settings registry key");
        key.SetValue("ProxyEnable", snapshot.ProxyEnable, RegistryValueKind.DWord);
        SetOrDelete(key, "ProxyServer", snapshot.ProxyServer);
        SetOrDelete(key, "ProxyOverride", snapshot.ProxyOverride);
        File.Delete(_snapshotPath);
        NotifyProxyChanged();
    }

    private void DisableIfManaged()
    {
        var current = ReadCurrent();
        if (current.ProxyServer?.Contains("127.0.0.1:208", StringComparison.OrdinalIgnoreCase) != true) return;

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open Windows Internet Settings registry key");
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        NotifyProxyChanged();
    }

    private static void SetOrDelete(RegistryKey key, string name, string? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.String);
    }

    private static ProxySnapshot ReadCurrent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath)
            ?? throw new InvalidOperationException("Unable to open Windows Internet Settings registry key");
        return new ProxySnapshot
        {
            ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable") ?? 0),
            ProxyServer = key.GetValue("ProxyServer") as string,
            ProxyOverride = key.GetValue("ProxyOverride") as string
        };
    }

    private static void NotifyProxyChanged()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private sealed class ProxySnapshot
    {
        public int ProxyEnable { get; set; }
        public string? ProxyServer { get; set; }
        public string? ProxyOverride { get; set; }
    }
}
