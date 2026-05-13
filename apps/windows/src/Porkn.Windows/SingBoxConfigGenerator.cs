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

    public static string Generate(Profile profile, int localProxyPort, RoutingSettings? routingSettings = null, Profile? chainEntryProfile = null)
    {
        routingSettings ??= RoutingSettings.Default;
        var routeRules = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["inbound"] = "mixed-in",
                ["action"] = "sniff",
                ["timeout"] = "1s"
            }
        };
        routeRules.AddRange(routingSettings.RouteRules().Select(rule => rule.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)));

        var outbounds = new List<Dictionary<string, object?>>();
        if (chainEntryProfile is not null)
        {
            outbounds.Add(BuildOutbound(chainEntryProfile, "chain-entry"));
            outbounds.Add(BuildOutbound(profile, "proxy-out", "chain-entry"));
        }
        else
        {
            outbounds.Add(BuildOutbound(profile));
        }
        outbounds.Add(new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" });
        outbounds.Add(new Dictionary<string, object?> { ["type"] = "block", ["tag"] = "block" });

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
            ["outbounds"] = outbounds,
            ["route"] = new Dictionary<string, object?>
            {
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = new Dictionary<string, object?>
                {
                    ["server"] = "local",
                    ["strategy"] = "prefer_ipv4"
                },
                ["rules"] = routeRules,
                ["final"] = "proxy-out"
            }
        };

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    private static Dictionary<string, object?> BuildOutbound(Profile profile, string tag = "proxy-out", string? detour = null)
    {
        return profile.Protocol.ToLowerInvariant() switch
        {
            "vless" => BuildVless(profile, tag, detour),
            "trojan" => BuildTrojan(profile, tag, detour),
            "socks" => BuildSocks(profile, tag, detour),
            _ => throw new NotSupportedException($"sing-box generator does not support {profile.Protocol.ToUpperInvariant()} yet")
        };
    }

    private static Dictionary<string, object?> BuildSocks(Profile profile, string tag, string? detour)
    {
        var outbound = BaseOutbound(profile, "socks", tag, detour);
        outbound["version"] = profile.Query.GetValueOrDefault("version", "5");
        outbound["network"] = "tcp";
        if (!string.IsNullOrWhiteSpace(profile.Username)) outbound["username"] = profile.Username;
        if (!string.IsNullOrWhiteSpace(profile.Password)) outbound["password"] = profile.Password;
        return outbound;
    }

    private static Dictionary<string, object?> BuildTrojan(Profile profile, string tag, string? detour)
    {
        var outbound = BaseOutbound(profile, "trojan", tag, detour);
        outbound["password"] = profile.Username ?? throw new InvalidOperationException("Trojan password is missing");
        outbound["tls"] = Tls(profile, defaultEnabled: true);
        return outbound;
    }

    private static Dictionary<string, object?> BuildVless(Profile profile, string tag, string? detour)
    {
        var outbound = BaseOutbound(profile, "vless", tag, detour);
        outbound["uuid"] = profile.Username ?? throw new InvalidOperationException("VLESS UUID is missing");
        outbound["network"] = profile.Query.GetValueOrDefault("network", "tcp");
        outbound["packet_encoding"] = profile.Query.GetValueOrDefault("packetEncoding", "xudp");
        if (profile.Query.TryGetValue("flow", out var flow) && !string.IsNullOrWhiteSpace(flow)) outbound["flow"] = flow;
        var tls = Tls(profile);
        if (tls is not null) outbound["tls"] = tls;
        var transport = Transport(profile);
        if (transport is not null) outbound["transport"] = transport;
        return outbound;
    }

    private static Dictionary<string, object?> BaseOutbound(Profile profile, string type, string tag, string? detour)
    {
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["tag"] = tag,
            ["server"] = profile.Host,
            ["server_port"] = profile.Port
        };
        if (!string.IsNullOrWhiteSpace(detour)) outbound["detour"] = detour;
        return outbound;
    }

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

        if (profile.Query.GetValueOrDefault("allowInsecure") == "1" || profile.Query.GetValueOrDefault("insecure") == "1")
        {
            tls["insecure"] = true;
        }

        return tls;
    }

    private static Dictionary<string, object?>? Transport(Profile profile)
    {
        var type = profile.Query.GetValueOrDefault("type", profile.Query.GetValueOrDefault("net", ""));
        if (string.IsNullOrWhiteSpace(type) || type == "tcp") return null;

        var transport = new Dictionary<string, object?> { ["type"] = type };
        if (profile.Query.TryGetValue("path", out var path) && !string.IsNullOrWhiteSpace(path)) transport["path"] = path;
        if (profile.Query.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            transport["headers"] = new Dictionary<string, object?> { ["Host"] = host };
        }
        if (profile.Query.TryGetValue("serviceName", out var serviceName) && !string.IsNullOrWhiteSpace(serviceName))
        {
            transport["service_name"] = serviceName;
        }
        return transport;
    }
}
