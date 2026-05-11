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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Endpoint => Port > 0 ? $"{Host}:{Port}" : Host;
    public override string ToString() => $"{Name}  ·  {Protocol.ToUpperInvariant()}  ·  {Endpoint}";
}
