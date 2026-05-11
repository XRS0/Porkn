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

    public static string Title(this ProfileSortMode mode) => mode.Title(AppLanguage.En);

    public static string Title(this ProfileSortMode mode, AppLanguage language) => mode switch
    {
        ProfileSortMode.FavoritesFirst => L10n.Text(language, "Сначала избранные", "Favorites first"),
        ProfileSortMode.FastestFirst => L10n.Text(language, "Сначала быстрые", "Fastest first"),
        ProfileSortMode.Name => L10n.Text(language, "По имени", "Name"),
        ProfileSortMode.RecentlyUsed => L10n.Text(language, "Недавно использованные", "Recently used"),
        _ => mode.ToString()
    };

    public static string Title(this SubscriptionAutoRefreshInterval interval) => interval.Title(AppLanguage.En);

    public static string Title(this SubscriptionAutoRefreshInterval interval, AppLanguage language) => interval switch
    {
        SubscriptionAutoRefreshInterval.Off => L10n.Text(language, "Выключено", "Off"),
        SubscriptionAutoRefreshInterval.SixHours => L10n.Text(language, "Каждые 6 часов", "Every 6 hours"),
        SubscriptionAutoRefreshInterval.TwelveHours => L10n.Text(language, "Каждые 12 часов", "Every 12 hours"),
        SubscriptionAutoRefreshInterval.Daily => L10n.Text(language, "Ежедневно", "Daily"),
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

    public static string Title(this RoutingPreset preset) => preset.Title(AppLanguage.En);

    public static string Title(this RoutingPreset preset, AppLanguage language) => preset switch
    {
        RoutingPreset.ProxyAll => L10n.Text(language, "Проксировать всё", "Proxy all"),
        RoutingPreset.DirectRuSu => L10n.Text(language, "Напрямую RU/SU", "Direct RU/SU"),
        RoutingPreset.DirectSelected => L10n.Text(language, "Выбранные напрямую", "Direct selected"),
        RoutingPreset.BypassLan => L10n.Text(language, "Обход LAN", "Bypass LAN"),
        RoutingPreset.Custom => L10n.Text(language, "Вручную", "Custom"),
        _ => preset.ToString()
    };

    public static string Detail(this RoutingPreset preset) => preset.Detail(AppLanguage.En);

    public static string Detail(this RoutingPreset preset, AppLanguage language) => preset switch
    {
        RoutingPreset.ProxyAll => L10n.Text(language, "Весь трафик идёт через proxy-out, кроме служебных правил sing-box.", "All traffic goes through proxy-out except sing-box service rules."),
        RoutingPreset.DirectRuSu => L10n.Text(language, "*.ru и *.su идут напрямую, остальное через proxy.", "*.ru and *.su go direct; everything else goes through proxy."),
        RoutingPreset.DirectSelected => L10n.Text(language, "Direct-домены идут напрямую, proxy/block группы применяются отдельно.", "Direct domains go directly; proxy/block groups are applied separately."),
        RoutingPreset.BypassLan => L10n.Text(language, "Локальные/private IP идут напрямую плюс пользовательские direct-домены.", "Local/private IP ranges go direct plus custom direct domains."),
        RoutingPreset.Custom => L10n.Text(language, "Полный ручной контроль direct/proxy/block групп доменов.", "Full manual control over direct/proxy/block domain groups."),
        _ => ""
    };
}
