using System.Text.Json;
using System.Text.Json.Serialization;

namespace Porkn.Windows;

internal enum AppLanguage
{
    Ru,
    En
}

internal enum ProfileSortMode
{
    FavoritesFirst,
    FastestFirst,
    Name,
    RecentlyUsed
}

internal enum SubscriptionAutoRefreshInterval
{
    Off,
    SixHours,
    TwelveHours,
    Daily
}

internal enum RoutingPreset
{
    ProxyAll,
    DirectRuSu,
    DirectSelected,
    BypassLan,
    Custom
}

internal sealed class AppSettings
{
    public bool LaunchAtLogin { get; set; }
    public bool AutoConnectLastProfile { get; set; }
    public bool KillSwitchEnabled { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.Ru;
    public string PreferredCore { get; set; } = "sing-box";
    public SubscriptionAutoRefreshInterval SubscriptionAutoRefreshInterval { get; set; } = SubscriptionAutoRefreshInterval.Off;
    public bool RefreshSubscriptionsOnLaunch { get; set; }
    public bool FavoritesOnly { get; set; }
    public ProfileSortMode ProfileSortMode { get; set; } = ProfileSortMode.FavoritesFirst;
    public Guid? LastSelectedProfileId { get; set; }
    public RoutingSettings Routing { get; set; } = RoutingSettings.Default;
}

internal sealed class SettingsStore
{
    private readonly string _filePath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsStore()
    {
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            Settings = new AppSettings();
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), _jsonOptions) ?? new AppSettings();
            Settings.Routing ??= RoutingSettings.Default;
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, _jsonOptions));
    }
}

internal static class L10n
{
    public static string Text(AppLanguage language, string ru, string en) => language == AppLanguage.Ru ? ru : en;
}

internal static class SettingTitles
{
    public static string Title(this AppLanguage language) => language switch
    {
        AppLanguage.Ru => "Русский",
        AppLanguage.En => "English",
        _ => language.ToString()
    };

    public static string Title(this ProfileSortMode mode) => mode switch
    {
        ProfileSortMode.FavoritesFirst => "Favorites first",
        ProfileSortMode.FastestFirst => "Fastest first",
        ProfileSortMode.Name => "Name",
        ProfileSortMode.RecentlyUsed => "Recently used",
        _ => mode.ToString()
    };

    public static string Title(this SubscriptionAutoRefreshInterval interval) => interval switch
    {
        SubscriptionAutoRefreshInterval.Off => "Off",
        SubscriptionAutoRefreshInterval.SixHours => "Every 6 hours",
        SubscriptionAutoRefreshInterval.TwelveHours => "Every 12 hours",
        SubscriptionAutoRefreshInterval.Daily => "Daily",
        _ => interval.ToString()
    };

    public static TimeSpan? TimeInterval(this SubscriptionAutoRefreshInterval interval) => interval switch
    {
        SubscriptionAutoRefreshInterval.Off => null,
        SubscriptionAutoRefreshInterval.SixHours => TimeSpan.FromHours(6),
        SubscriptionAutoRefreshInterval.TwelveHours => TimeSpan.FromHours(12),
        SubscriptionAutoRefreshInterval.Daily => TimeSpan.FromDays(1),
        _ => null
    };

    public static string Title(this RoutingPreset preset) => preset switch
    {
        RoutingPreset.ProxyAll => "Proxy all",
        RoutingPreset.DirectRuSu => "Direct RU/SU",
        RoutingPreset.DirectSelected => "Direct selected",
        RoutingPreset.BypassLan => "Bypass LAN",
        RoutingPreset.Custom => "Custom",
        _ => preset.ToString()
    };

    public static string Detail(this RoutingPreset preset) => preset switch
    {
        RoutingPreset.ProxyAll => "Весь трафик идёт через proxy-out, кроме служебных правил sing-box.",
        RoutingPreset.DirectRuSu => "*.ru и *.su идут напрямую, остальное через proxy.",
        RoutingPreset.DirectSelected => "Direct domains идут напрямую, proxy/block группы применяются отдельно.",
        RoutingPreset.BypassLan => "Локальные/private IP идут напрямую плюс пользовательские direct domains.",
        RoutingPreset.Custom => "Полный ручной контроль direct/proxy/block domain groups.",
        _ => ""
    };
}
