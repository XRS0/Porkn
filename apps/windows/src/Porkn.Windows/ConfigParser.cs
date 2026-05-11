using System.Text;
using System.Text.Json;

namespace Porkn.Windows;

internal static class ConfigParser
{
    public static async Task<List<Profile>> ImportAsync(string text, CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return [];

        if (LooksLikeSubscriptionUrl(text))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var body = await http.GetStringAsync(new Uri(text), cancellationToken);
            return ParseMany(DecodeSubscriptionBody(body));
        }

        return ParseMany(text);
    }

    public static bool LooksLikeSubscriptionUrl(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Contains('\n') || trimmed.Contains('\r')) return false;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var subscriptionUri)
            && (subscriptionUri.Scheme == "http" || subscriptionUri.Scheme == "https");
    }

    public static Subscription ParseSubscription(string text)
    {
        var uri = new Uri(text.Trim());
        var name = string.IsNullOrWhiteSpace(uri.Fragment)
            ? $"Subscription · {uri.Host}"
            : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        return new Subscription { Name = name, Url = uri.ToString() };
    }

    public static List<Profile> ParseMany(string text)
    {
        text = DecodeSubscriptionBody(text);
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(ParseOne)
            .Where(profile => profile is not null)
            .Cast<Profile>()
            .ToList();
    }

    public static Profile? ParseOne(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return ParseVmess(raw);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme.ToLowerInvariant() switch
        {
            "vless" => ParseProxyUrl(uri, raw, "vless"),
            "socks" or "socks5" => ParseProxyUrl(uri, raw, "socks"),
            "trojan" => ParseProxyUrl(uri, raw, "trojan"),
            "ss" or "shadowsocks" => ParseProxyUrl(uri, raw, "shadowsocks"),
            _ => null
        };
    }

    public static string DecodeSubscriptionBody(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Contains("://")) return trimmed;

        try
        {
            var padded = trimmed.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return decoded.Contains("://") ? decoded : body;
        }
        catch
        {
            return body;
        }
    }

    private static Profile ParseProxyUrl(Uri uri, string raw, string protocol)
    {
        var query = ParseQuery(uri.Query);
        var (username, password) = ParseUserInfo(uri.UserInfo);
        return new Profile
        {
            Name = string.IsNullOrWhiteSpace(uri.Fragment) ? $"{protocol.ToUpperInvariant()} · {uri.Host}" : Uri.UnescapeDataString(uri.Fragment.TrimStart('#')),
            Protocol = protocol,
            Host = string.IsNullOrWhiteSpace(uri.Host) ? HostFromOpaqueUrl(raw) ?? "unknown" : uri.Host,
            Port = uri.Port > 0 ? uri.Port : PortFromOpaqueUrl(raw) ?? DefaultPort(protocol),
            RawConfig = raw,
            Username = username,
            Password = password,
            Query = query
        };
    }

    private static Profile? ParseVmess(string raw)
    {
        try
        {
            var payload = raw["vmess://".Length..];
            var padded = payload.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var host = GetString(root, "add") ?? GetString(root, "host") ?? "unknown";
            var port = GetInt(root, "port") ?? DefaultPort("vmess");
            var name = GetString(root, "ps") ?? $"VMess · {host}";
            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                query[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.ToString()
                };
            }

            return new Profile
            {
                Name = name,
                Protocol = "vmess",
                Host = host,
                Port = port,
                RawConfig = raw,
                Username = GetString(root, "id"),
                Password = GetString(root, "aid"),
                Query = query
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) ? value.ToString() : null;
    }

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
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

    private static string? HostFromOpaqueUrl(string raw)
    {
        var at = raw.LastIndexOf('@');
        if (at < 0 || at + 1 >= raw.Length) return null;
        var afterAt = raw[(at + 1)..].Split('?', 2)[0].Split('#', 2)[0];
        return afterAt.Split(':', 2)[0];
    }

    private static int? PortFromOpaqueUrl(string raw)
    {
        var at = raw.LastIndexOf('@');
        if (at < 0 || at + 1 >= raw.Length) return null;
        var afterAt = raw[(at + 1)..].Split('?', 2)[0].Split('#', 2)[0];
        var parts = afterAt.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var port) ? port : null;
    }

    private static int DefaultPort(string protocol) => protocol switch
    {
        "trojan" or "vless" or "vmess" => 443,
        "shadowsocks" => 8388,
        _ => 1080
    };
}
