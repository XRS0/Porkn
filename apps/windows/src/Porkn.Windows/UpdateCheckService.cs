using System.Text.Json;

namespace Porkn.Windows;

internal sealed class UpdateCheckResult
{
    public string LatestVersion { get; init; } = "";
    public string LocalVersion { get; init; } = "";
    public string ReleaseUrl { get; init; } = "https://github.com/XRS0/Porkn/releases/latest";
    public bool IsUpdateAvailable { get; init; }

    public string Title => IsUpdateAvailable ? $"Update available: {LatestVersion}" : "porkn is up to date";
    public string Detail => IsUpdateAvailable ? $"Installed: {LocalVersion}. Latest: {LatestVersion}." : $"Installed: {LocalVersion}.";
}

internal sealed class UpdateCheckService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/XRS0/Porkn/releases/latest");

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("porkn-windows");
        var body = await http.GetStringAsync(LatestReleaseUri, cancellationToken);
        using var document = JsonDocument.Parse(body);
        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "v0.0.0";
        var url = document.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/XRS0/Porkn/releases/latest";
        var local = typeof(UpdateCheckService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new UpdateCheckResult
        {
            LatestVersion = tag,
            LocalVersion = local,
            ReleaseUrl = url,
            IsUpdateAvailable = CompareVersions(tag, local) > 0
        };
    }

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
