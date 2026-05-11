using System.Text.Json;
using System.Text.Json.Serialization;

namespace Porkn.Windows;

internal static class SingBoxConfigGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Generate(Profile profile, int localProxyPort)
    {
        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = "info",
                ["timestamp"] = true
            },
            ["dns"] = new Dictionary<string, object?>
            {
                ["servers"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "https",
                        ["tag"] = "cloudflare",
                        ["server"] = "1.1.1.1",
                        ["server_port"] = 443,
                        ["path"] = "/dns-query",
                        ["tls"] = new Dictionary<string, object?> { ["server_name"] = "cloudflare-dns.com" }
                    },
                    new Dictionary<string, object?> { ["type"] = "local", ["tag"] = "local" }
                },
                ["final"] = "cloudflare"
            },
            ["inbounds"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = localProxyPort
                }
            },
            ["outbounds"] = new object[]
            {
                BuildOutbound(profile),
                new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" },
                new Dictionary<string, object?> { ["type"] = "block", ["tag"] = "block" }
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["inbound"] = "mixed-in",
                        ["action"] = "sniff",
                        ["timeout"] = "1s"
                    }
                },
                ["final"] = "proxy-out"
            }
        };

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    private static Dictionary<string, object?> BuildOutbound(Profile profile)
    {
        return profile.Protocol.ToLowerInvariant() switch
        {
            "vless" => BuildVless(profile),
            "trojan" => BuildTrojan(profile),
            "socks" => BuildSocks(profile),
            _ => throw new NotSupportedException($"Unsupported protocol: {profile.Protocol}")
        };
    }

    private static Dictionary<string, object?> BuildSocks(Profile profile)
    {
        var outbound = BaseOutbound(profile, "socks");
        outbound["version"] = profile.Query.GetValueOrDefault("version", "5");
        outbound["network"] = "tcp";
        if (!string.IsNullOrWhiteSpace(profile.Username)) outbound["username"] = profile.Username;
        if (!string.IsNullOrWhiteSpace(profile.Password)) outbound["password"] = profile.Password;
        return outbound;
    }

    private static Dictionary<string, object?> BuildTrojan(Profile profile)
    {
        var outbound = BaseOutbound(profile, "trojan");
        outbound["password"] = profile.Username ?? throw new InvalidOperationException("Trojan password is missing");
        outbound["tls"] = Tls(profile, defaultEnabled: true);
        return outbound;
    }

    private static Dictionary<string, object?> BuildVless(Profile profile)
    {
        var outbound = BaseOutbound(profile, "vless");
        outbound["uuid"] = profile.Username ?? throw new InvalidOperationException("VLESS UUID is missing");
        outbound["network"] = profile.Query.GetValueOrDefault("network", profile.Query.GetValueOrDefault("type", "tcp"));
        outbound["packet_encoding"] = profile.Query.GetValueOrDefault("packetEncoding", "xudp");
        if (profile.Query.TryGetValue("flow", out var flow) && !string.IsNullOrWhiteSpace(flow)) outbound["flow"] = flow;
        var tls = Tls(profile);
        if (tls is not null) outbound["tls"] = tls;
        return outbound;
    }

    private static Dictionary<string, object?> BaseOutbound(Profile profile, string type) => new()
    {
        ["type"] = type,
        ["tag"] = "proxy-out",
        ["server"] = profile.Host,
        ["server_port"] = profile.Port
    };

    private static Dictionary<string, object?>? Tls(Profile profile, bool defaultEnabled = false)
    {
        var security = profile.Query.GetValueOrDefault("security", profile.Query.GetValueOrDefault("tls", ""));
        var enabled = defaultEnabled || security is "tls" or "reality";
        if (!enabled) return null;

        var tls = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["server_name"] = profile.Query.GetValueOrDefault("sni", profile.Query.GetValueOrDefault("peer", profile.Host))
        };

        if (profile.Query.TryGetValue("fp", out var fp) && !string.IsNullOrWhiteSpace(fp))
        {
            tls["utls"] = new Dictionary<string, object?> { ["enabled"] = true, ["fingerprint"] = fp };
        }

        if (security == "reality")
        {
            var reality = new Dictionary<string, object?> { ["enabled"] = true };
            if (profile.Query.TryGetValue("pbk", out var publicKey)) reality["public_key"] = publicKey;
            if (profile.Query.TryGetValue("sid", out var shortId)) reality["short_id"] = shortId;
            tls["reality"] = reality;
        }

        return tls;
    }
}
