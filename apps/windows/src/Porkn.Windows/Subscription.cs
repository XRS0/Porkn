namespace Porkn.Windows;

internal sealed class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Subscription";
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRefreshAt { get; set; }
    public int LastImportedCount { get; set; }
}

internal sealed class SubscriptionRefreshSummary
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Total { get; set; }
    public string SubscriptionName { get; set; } = "Subscription";
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ShortText => $"{SubscriptionName}: +{Added} / ~{Updated} / -{Removed}, total {Total}";
}

internal sealed class ImportResult
{
    public int ProfilesImported { get; init; }
    public Subscription? Subscription { get; init; }
    public SubscriptionRefreshSummary? Summary { get; init; }

    public string Message => Summary is not null
        ? Summary.ShortText
        : $"Imported {ProfilesImported} profile(s)";
}
