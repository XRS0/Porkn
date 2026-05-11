using System.Text.Json;
using System.Text.Json.Serialization;

namespace Porkn.Windows;

internal sealed class RoutingSettings : IEquatable<RoutingSettings>
{
    public const string DefaultDirectDomainsText = "*.ru\n*.su";

    public RoutingPreset Preset { get; set; } = RoutingPreset.DirectRuSu;
    public string DirectDomainsText { get; set; } = DefaultDirectDomainsText;
    public string ProxyDomainsText { get; set; } = "";
    public string BlockDomainsText { get; set; } = "";

    public static RoutingSettings Default => new();

    public string EffectiveDirectDomainsText => Preset switch
    {
        RoutingPreset.ProxyAll => "",
        RoutingPreset.DirectRuSu => DefaultDirectDomainsText,
        _ => DirectDomainsText
    };

    public List<Dictionary<string, object>> RouteRules()
    {
        var rules = new List<Dictionary<string, object>>();
        if (Preset == RoutingPreset.BypassLan)
        {
            rules.Add(new Dictionary<string, object> { ["ip_is_private"] = true, ["outbound"] = "direct" });
        }

        AddDomainRule(rules, BlockDomainsText, "block");
        AddDomainRule(rules, EffectiveDirectDomainsText, "direct");
        AddDomainRule(rules, ProxyDomainsText, "proxy-out");
        return rules;
    }

    public string ExportJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });
    }

    public static RoutingSettings ImportJson(string text)
    {
        return JsonSerializer.Deserialize<RoutingSettings>(text, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        }) ?? Default;
    }

    public bool Equals(RoutingSettings? other)
    {
        if (other is null) return false;
        return Preset == other.Preset
            && string.Equals(DirectDomainsText, other.DirectDomainsText, StringComparison.Ordinal)
            && string.Equals(ProxyDomainsText, other.ProxyDomainsText, StringComparison.Ordinal)
            && string.Equals(BlockDomainsText, other.BlockDomainsText, StringComparison.Ordinal);
    }

    public RoutingSettings Clone() => new()
    {
        Preset = Preset,
        DirectDomainsText = DirectDomainsText,
        ProxyDomainsText = ProxyDomainsText,
        BlockDomainsText = BlockDomainsText
    };

    private static void AddDomainRule(List<Dictionary<string, object>> rules, string text, string outbound)
    {
        var parsed = DomainRuleParser.Parse(text);
        if (parsed.DomainSuffix.Count == 0 && parsed.Domain.Count == 0) return;

        var rule = new Dictionary<string, object> { ["outbound"] = outbound };
        if (parsed.DomainSuffix.Count > 0) rule["domain_suffix"] = parsed.DomainSuffix;
        if (parsed.Domain.Count > 0) rule["domain"] = parsed.Domain;
        rules.Add(rule);
    }
}

internal sealed class ParsedDomainRules
{
    public List<string> Domain { get; } = [];
    public List<string> DomainSuffix { get; } = [];
}

internal static class DomainRuleParser
{
    public static ParsedDomainRules Parse(string text)
    {
        var parsed = new ParsedDomainRules();
        var seenSuffix = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokens(text))
        {
            var normalized = Normalize(token);
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            if (seenSuffix.Add(normalized)) parsed.DomainSuffix.Add(normalized);
        }
        return parsed;
    }

    public static string AppendDomains(string text, IEnumerable<string> domains)
    {
        var parsed = Parse(text).DomainSuffix;
        foreach (var domain in domains.SelectMany(domain => Parse(domain).DomainSuffix))
        {
            if (!parsed.Contains(domain, StringComparer.OrdinalIgnoreCase)) parsed.Add(domain);
        }
        return string.Join(Environment.NewLine, parsed.Select(domain => $"*.{domain}"));
    }

    private static IEnumerable<string> Tokens(string text)
    {
        return text
            .Replace('，', ',')
            .Split([',', '\n', '\r', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token) && !token.StartsWith('#'));
    }

    private static string? Normalize(string token)
    {
        var value = token.Trim().ToLowerInvariant();
        value = StripPrefix(value, "http://");
        value = StripPrefix(value, "https://");
        value = value.Split('/', 2)[0];
        value = value.Split(':', 2)[0];
        if (value.StartsWith("*.")) value = value[2..];
        value = value.Trim('.');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string StripPrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;
    }
}
