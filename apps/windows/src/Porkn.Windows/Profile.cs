namespace Porkn.Windows;

internal sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Profile";
    public string Protocol { get; set; } = "socks";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string RawConfig { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public Dictionary<string, string> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Guid? SubscriptionId { get; set; }
    public string? SubscriptionKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? LastPingMilliseconds { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public string Endpoint => Port > 0 ? $"{Host}:{Port}" : Host;

    public string StableKey => !string.IsNullOrWhiteSpace(SubscriptionKey)
        ? SubscriptionKey
        : MakeStableKey(this);

    public override string ToString() => $"{Name}  ·  {Protocol.ToUpperInvariant()}  ·  {Endpoint}";

    public static string MakeStableKey(Profile profile)
    {
        var normalizedUser = Uri.UnescapeDataString(profile.Username ?? "").ToLowerInvariant();
        var normalizedHost = profile.Host.ToLowerInvariant();
        var normalizedPort = profile.Port > 0 ? profile.Port.ToString() : "";
        if (!string.IsNullOrWhiteSpace(normalizedHost) && normalizedHost != "unknown")
        {
            return string.Join('|', profile.Protocol.ToLowerInvariant(), normalizedUser, normalizedHost, normalizedPort);
        }

        return string.Join('|', profile.Protocol.ToLowerInvariant(), NormalizeRawConfig(profile.RawConfig));
    }

    private static string NormalizeRawConfig(string rawConfig)
    {
        var value = rawConfig.Trim();
        var hashIndex = value.IndexOf('#');
        if (hashIndex >= 0) value = value[..hashIndex];
        return value.ToLowerInvariant();
    }
}
