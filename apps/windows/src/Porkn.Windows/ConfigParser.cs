using System.Text;

namespace Porkn.Windows;

internal static class ConfigParser
{
    public static async Task<List<Profile>> ImportAsync(string text, CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return [];

        if (Uri.TryCreate(text, UriKind.Absolute, out var subscriptionUri)
            && (subscriptionUri.Scheme == "http" || subscriptionUri.Scheme == "https"))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var body = await http.GetStringAsync(subscriptionUri, cancellationToken);
            return ParseMany(DecodeSubscriptionBody(body));
        }

        return ParseMany(text);
    }

    public static List<Profile> ParseMany(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseOne)
            .Where(profile => profile is not null)
            .Cast<Profile>()
            .ToList();
    }

    private static Profile? ParseOne(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme.ToLowerInvariant() switch
        {
            "vless" => ParseProxyUrl(uri, raw, "vless"),
            "socks" or "socks5" => ParseProxyUrl(uri, raw, "socks"),
            "trojan" => ParseProxyUrl(uri, raw, "trojan"),
            _ => null
        };
    }

    private static Profile ParseProxyUrl(Uri uri, string raw, string protocol)
    {
        var query = ParseQuery(uri.Query);
        var (username, password) = ParseUserInfo(uri.UserInfo);
        return new Profile
        {
            Name = string.IsNullOrWhiteSpace(uri.Fragment) ? $"{protocol.ToUpperInvariant()} {uri.Host}" : Uri.UnescapeDataString(uri.Fragment.TrimStart('#')),
            Protocol = protocol,
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : DefaultPort(protocol),
            RawConfig = raw,
            Username = username,
            Password = password,
            Query = query
        };
    }

    private static (string? Username, string? Password) ParseUserInfo(string userInfo)
    {
        if (string.IsNullOrWhiteSpace(userInfo)) return (null, null);
        var decoded = Uri.UnescapeDataString(userInfo);
        var parts = decoded.Split(':', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (decoded, null);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        query = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query)) return result;

        foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
            result[key] = value;
        }

        return result;
    }

    private static string DecodeSubscriptionBody(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Contains("://")) return trimmed;

        try
        {
            var padded = trimmed.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch
        {
            return body;
        }
    }

    private static int DefaultPort(string protocol) => protocol switch
    {
        "trojan" or "vless" => 443,
        _ => 1080
    };
}
