using System.Text.Json;

namespace Porkn.Windows;

internal sealed class ProfileStore
{
    private readonly string _profilesPath;
    private readonly string _subscriptionsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public List<Profile> Profiles { get; private set; } = [];
    public List<Subscription> Subscriptions { get; private set; } = [];
    public SubscriptionRefreshSummary? LastRefreshSummary { get; private set; }

    public ProfileStore()
    {
        _profilesPath = Path.Combine(AppPaths.DataDirectory, "profiles.json");
        _subscriptionsPath = Path.Combine(AppPaths.DataDirectory, "subscriptions.json");
        Load();
    }

    public void Load()
    {
        Profiles = ReadList<Profile>(_profilesPath);
        Subscriptions = ReadList<Subscription>(_subscriptionsPath);
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(Profiles, _jsonOptions));
        File.WriteAllText(_subscriptionsPath, JsonSerializer.Serialize(Subscriptions, _jsonOptions));
    }

    public async Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken = default)
    {
        if (ConfigParser.LooksLikeSubscriptionUrl(text))
        {
            var subscription = UpsertSubscription(ConfigParser.ParseSubscription(text));
            var summary = await RefreshWithSummaryAsync(subscription, cancellationToken);
            return new ImportResult { ProfilesImported = summary.Total, Subscription = subscription, Summary = summary };
        }

        var profiles = ConfigParser.ParseMany(text);
        UpsertManualProfiles(profiles);
        Save();
        return new ImportResult { ProfilesImported = profiles.Count };
    }

    public async Task<SubscriptionRefreshSummary> RefreshWithSummaryAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var body = await http.GetStringAsync(subscription.Url, cancellationToken);
        return Refresh(subscription, body);
    }

    public SubscriptionRefreshSummary Refresh(Subscription subscription, string body)
    {
        var imported = ConfigParser.ParseMany(ConfigParser.DecodeSubscriptionBody(body));
        foreach (var profile in imported)
        {
            profile.SubscriptionId = subscription.Id;
            profile.SubscriptionKey = Profile.MakeStableKey(profile);
        }

        var diff = UpsertSubscriptionProfiles(imported, subscription.Id);
        var refreshedAt = DateTimeOffset.UtcNow;
        var existing = Subscriptions.FirstOrDefault(item => item.Id == subscription.Id);
        if (existing is not null)
        {
            existing.LastRefreshAt = refreshedAt;
            existing.LastImportedCount = imported.Count;
        }

        var summary = new SubscriptionRefreshSummary
        {
            Added = diff.Added,
            Updated = diff.Updated,
            Removed = diff.Removed,
            Total = imported.Count,
            SubscriptionName = subscription.Name,
            RefreshedAt = refreshedAt
        };
        LastRefreshSummary = summary;
        Save();
        return summary;
    }

    public async Task RefreshSubscriptionsIfNeededAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.RefreshSubscriptionsOnLaunch && settings.SubscriptionAutoRefreshInterval == SubscriptionAutoRefreshInterval.Off) return;
        foreach (var subscription in Subscriptions.ToList())
        {
            if (!ShouldRefresh(subscription, settings)) continue;
            try
            {
                await RefreshWithSummaryAsync(subscription, cancellationToken);
            }
            catch
            {
                // The UI logs manual refresh errors; launch refresh should not block startup.
            }
        }
    }

    public void Delete(Profile profile)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        Save();
    }

    public void Delete(Subscription subscription)
    {
        Subscriptions.RemoveAll(item => item.Id == subscription.Id);
        Profiles.RemoveAll(profile => profile.SubscriptionId == subscription.Id);
        Save();
    }


    public ImportResult ImportRasPhonebook(string path)
    {
        var profiles = RasPhonebookImporter.Import(path);
        UpsertManualProfiles(profiles);
        Save();
        return new ImportResult { ProfilesImported = profiles.Count };
    }

    public void AddManualSocks(string name, string host, int port, string? username, string? password)
    {
        var auth = string.IsNullOrWhiteSpace(username)
            ? ""
            : string.IsNullOrWhiteSpace(password)
                ? $"{Uri.EscapeDataString(username)}@"
                : $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@";
        var raw = $"socks://{auth}{host}:{port}#{Uri.EscapeDataString(name)}";
        UpsertManualProfiles([
            new Profile
            {
                Name = name,
                Protocol = "socks",
                Host = host,
                Port = port,
                RawConfig = raw,
                Username = username,
                Password = password,
                Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["version"] = "5" }
            }
        ]);
        Save();
    }

    public void ToggleFavorite(Profile profile)
    {
        var existing = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is null) return;
        existing.IsFavorite = !existing.IsFavorite;
        Save();
    }

    public void MarkUsed(Profile profile, DateTimeOffset? date = null)
    {
        var existing = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is null) return;
        existing.LastUsedAt = date ?? DateTimeOffset.UtcNow;
        Save();
    }

    public void UpdatePing(Profile profile, int? milliseconds)
    {
        var existing = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is null) return;
        existing.LastPingMilliseconds = milliseconds;
        Save();
    }

    public Profile? SelectFastestProfile()
    {
        return Profiles
            .Where(profile => profile.LastPingMilliseconds.HasValue)
            .OrderBy(profile => profile.LastPingMilliseconds!.Value)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public IEnumerable<Profile> FilteredProfiles(string searchText, bool favoritesOnly, ProfileSortMode sortMode)
    {
        var query = searchText.Trim();
        IEnumerable<Profile> result = Profiles.Where(profile =>
            (!favoritesOnly || profile.IsFavorite)
            && (string.IsNullOrWhiteSpace(query)
                || profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || profile.Host.Contains(query, StringComparison.OrdinalIgnoreCase)
                || profile.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
                || profile.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase)
                || SubscriptionNameFor(profile).Contains(query, StringComparison.OrdinalIgnoreCase)));

        result = sortMode switch
        {
            ProfileSortMode.FavoritesFirst => result
                .OrderByDescending(profile => profile.IsFavorite)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase),
            ProfileSortMode.FastestFirst => result
                .OrderBy(profile => profile.LastPingMilliseconds ?? int.MaxValue)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase),
            ProfileSortMode.RecentlyUsed => result
                .OrderByDescending(profile => profile.LastUsedAt ?? DateTimeOffset.MinValue)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase),
            _ => result.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
        };
        return result;
    }

    public string SubscriptionNameFor(Profile profile)
    {
        if (profile.SubscriptionId is not Guid id) return "Manual";
        return Subscriptions.FirstOrDefault(subscription => subscription.Id == id)?.Name ?? "Subscription";
    }

    private Subscription UpsertSubscription(Subscription incoming)
    {
        var existing = Subscriptions.FirstOrDefault(subscription => string.Equals(subscription.Url, incoming.Url, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Subscriptions.Add(incoming);
            Save();
            return incoming;
        }

        existing.Name = incoming.Name;
        Save();
        return existing;
    }

    private void UpsertManualProfiles(IEnumerable<Profile> incomingProfiles)
    {
        foreach (var profile in incomingProfiles)
        {
            var key = Profile.MakeStableKey(profile);
            var existing = Profiles.FirstOrDefault(item => item.SubscriptionId is null && Profile.MakeStableKey(item) == key);
            if (existing is null)
            {
                Profiles.Add(profile);
                continue;
            }

            Merge(existing, profile);
        }
    }

    private (int Added, int Updated, int Removed) UpsertSubscriptionProfiles(IEnumerable<Profile> incomingProfiles, Guid subscriptionId)
    {
        var incoming = incomingProfiles.ToList();
        var incomingKeys = incoming.Select(profile => profile.StableKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        foreach (var profile in incoming)
        {
            var existing = Profiles.FirstOrDefault(item => item.SubscriptionId == subscriptionId && item.StableKey == profile.StableKey);
            if (existing is null)
            {
                Profiles.Add(profile);
                added += 1;
                continue;
            }

            Merge(existing, profile);
            updated += 1;
        }

        var removed = Profiles.Count(profile => profile.SubscriptionId == subscriptionId && !incomingKeys.Contains(profile.StableKey));
        Profiles.RemoveAll(profile => profile.SubscriptionId == subscriptionId && !incomingKeys.Contains(profile.StableKey));
        return (added, updated, removed);
    }

    private static void Merge(Profile existing, Profile incoming)
    {
        var id = existing.Id;
        var createdAt = existing.CreatedAt;
        var ping = existing.LastPingMilliseconds;
        var favorite = existing.IsFavorite;
        var lastUsed = existing.LastUsedAt;

        existing.Name = incoming.Name;
        existing.Protocol = incoming.Protocol;
        existing.Host = incoming.Host;
        existing.Port = incoming.Port;
        existing.RawConfig = incoming.RawConfig;
        existing.Username = incoming.Username;
        existing.Password = incoming.Password;
        existing.Query = incoming.Query;
        existing.SubscriptionId = incoming.SubscriptionId ?? existing.SubscriptionId;
        existing.SubscriptionKey = incoming.SubscriptionKey ?? existing.SubscriptionKey;
        existing.Id = id;
        existing.CreatedAt = createdAt;
        existing.LastPingMilliseconds = ping;
        existing.IsFavorite = favorite;
        existing.LastUsedAt = lastUsed;
    }

    private static bool ShouldRefresh(Subscription subscription, AppSettings settings)
    {
        if (settings.RefreshSubscriptionsOnLaunch) return true;
        var interval = settings.SubscriptionAutoRefreshInterval.TimeInterval();
        if (interval is null) return false;
        if (subscription.LastRefreshAt is null) return true;
        return DateTimeOffset.UtcNow - subscription.LastRefreshAt >= interval;
    }

    private static List<T> ReadList<T>(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
