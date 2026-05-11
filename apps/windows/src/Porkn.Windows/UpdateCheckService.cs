using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Porkn.Windows;

internal sealed class UpdateCheckResult
{
    public string LatestVersion { get; init; } = "";
    public string LocalVersion { get; init; } = "";
    public string ReleaseUrl { get; init; } = "https://github.com/XRS0/Porkn/releases/latest";
    public string? AssetName { get; init; }
    public string? AssetUrl { get; init; }
    public string? Sha256 { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public bool CanInstall => IsUpdateAvailable && !string.IsNullOrWhiteSpace(AssetUrl);

    public string Title => IsUpdateAvailable ? $"Update available: {LatestVersion}" : "porkn is up to date";
    public string Detail => IsUpdateAvailable ? $"Installed: {LocalVersion}. Latest: {LatestVersion}." : $"Installed: {LocalVersion}.";
}

internal sealed class UpdateInstallResult
{
    public string Version { get; init; } = "";
    public string AssetPath { get; init; } = "";
    public bool RelaunchStarted { get; init; }
}

internal sealed class UpdateCheckService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/XRS0/Porkn/releases/latest");
    private const string WindowsAssetName = "porkn-windows-x64.zip";
    private const string WindowsShaName = "SHA256SUMS-windows.txt";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var http = CreateHttpClient();
        var body = await http.GetStringAsync(LatestReleaseUri, cancellationToken);
        return await ParseAsync(body, http, cancellationToken);
    }

    public async Task<UpdateInstallResult> DownloadAndInstallAsync(UpdateCheckResult update, Action<string>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (!update.CanInstall || string.IsNullOrWhiteSpace(update.AssetUrl))
        {
            throw new InvalidOperationException("No installable Windows update asset was found in the latest release.");
        }

        using var http = CreateHttpClient(timeoutSeconds: 120);
        var updateDir = Path.Combine(AppPaths.DataDirectory, "Updates", update.LatestVersion);
        var extractDir = Path.Combine(updateDir, "extracted");
        var zipPath = Path.Combine(updateDir, WindowsAssetName);
        var scriptPath = Path.Combine(updateDir, "install-porkn-update.ps1");
        Directory.CreateDirectory(updateDir);
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);

        onProgress?.Invoke("Downloading update package…");
        await using (var input = await http.GetStreamAsync(update.AssetUrl, cancellationToken))
        await using (var output = File.Create(zipPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            onProgress?.Invoke("Verifying SHA256 checksum…");
            var actual = await ComputeSha256Async(zipPath, cancellationToken);
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Update checksum mismatch. Expected {update.Sha256}, got {actual}.");
            }
        }

        onProgress?.Invoke("Extracting update package…");
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        var sourceDir = Directory.GetDirectories(extractDir).FirstOrDefault(path => Path.GetFileName(path).Equals("porkn-windows-x64", StringComparison.OrdinalIgnoreCase)) ?? extractDir;
        var sourceExe = Path.Combine(sourceDir, "porkn.exe");
        if (!File.Exists(sourceExe)) throw new FileNotFoundException("Downloaded update does not contain porkn.exe", sourceExe);

        var appDir = AppPaths.AppDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        WriteInstallerScript(scriptPath, sourceDir, appDir, Environment.ProcessId);

        onProgress?.Invoke("Starting updater and closing porkn…");
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return new UpdateInstallResult { Version = update.LatestVersion, AssetPath = zipPath, RelaunchStarted = true };
    }

    private static async Task<UpdateCheckResult> ParseAsync(string body, HttpClient http, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "v0.0.0";
        var url = root.GetProperty("html_url").GetString() ?? "https://github.com/XRS0/Porkn/releases/latest";
        var local = typeof(UpdateCheckService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var assetUrl = FindAssetDownloadUrl(root, WindowsAssetName);
        var shaAssetUrl = FindAssetDownloadUrl(root, WindowsShaName);
        var sha = shaAssetUrl is null ? null : ParseSha256(await http.GetStringAsync(shaAssetUrl, cancellationToken), WindowsAssetName);

        return new UpdateCheckResult
        {
            LatestVersion = tag,
            LocalVersion = local,
            ReleaseUrl = url,
            AssetName = assetUrl is null ? null : WindowsAssetName,
            AssetUrl = assetUrl,
            Sha256 = sha,
            IsUpdateAvailable = CompareVersions(tag, local) > 0
        };
    }

    private static string? FindAssetDownloadUrl(JsonElement root, string assetName)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (!assetName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return asset.GetProperty("browser_download_url").GetString();
        }
        return null;
    }

    private static string? ParseSha256(string sumsText, string assetName)
    {
        foreach (var line in sumsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            if (Path.GetFileName(parts[^1]).Equals(assetName, StringComparison.OrdinalIgnoreCase)) return parts[0];
        }
        return null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteInstallerScript(string scriptPath, string sourceDir, string appDir, int processId)
    {
        var exePath = Path.Combine(appDir, "porkn.exe");
        var script = $$"""
$ErrorActionPreference = 'Stop'
$source = '{{EscapePowerShell(sourceDir)}}'
$target = '{{EscapePowerShell(appDir)}}'
$exe = '{{EscapePowerShell(exePath)}}'
$pidToWait = {{processId}}
try {
  Wait-Process -Id $pidToWait -Timeout 30 -ErrorAction SilentlyContinue
} catch {}
Start-Sleep -Milliseconds 600
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
Start-Process -FilePath $exe
""";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static HttpClient CreateHttpClient(int timeoutSeconds = 12)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("porkn-windows");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static int CompareVersions(string left, string right)
    {
        static int[] Parts(string version) => version.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
        var l = Parts(left);
        var r = Parts(right);
        for (var i = 0; i < Math.Max(l.Length, r.Length); i++)
        {
            var lv = i < l.Length ? l[i] : 0;
            var rv = i < r.Length ? r[i] : 0;
            if (lv != rv) return lv.CompareTo(rv);
        }
        return 0;
    }
}
