using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Porkn.Windows;

internal enum MainSelectionKind
{
    Profile,
    Settings
}

internal enum SettingsTab
{
    General,
    Routing
}

internal sealed class MainWindow : Window
{
    private const string LocalProxyHost = "127.0.0.1";

    private readonly ProfileStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly SingBoxProcessManager _singBox = new();
    private readonly WindowsProxyManager _proxy = new();
    private readonly PingService _pingService = new();
    private readonly ProxyHealthCheckService _healthCheck = new();
    private readonly RasDialManager _rasDial = new();

    private readonly StackPanel _subscriptionsPanel = new() { Spacing = 8 };
    private readonly StackPanel _profilesPanel = new() { Spacing = 8 };
    private readonly ContentControl _detailHost = new();
    private readonly TextBox _searchText = new();
    private readonly TextBox _importText = new();
    private readonly CheckBox _favoritesOnly = new();
    private readonly ComboBox _sortMode = new();
    private TextBox? _logTextControl;

    private MainSelectionKind _selectionKind = MainSelectionKind.Profile;
    private SettingsTab _settingsTab = SettingsTab.General;
    private Profile? _selectedProfile;
    private Profile? _activeProfile;
    private ProxyHealthStatus _healthStatus = ProxyHealthStatus.NotChecked();
    private readonly List<string> _logLines = [];
    private bool _isConnected;
    private bool _isBusy;
    private bool _manualStopInProgress;
    private int _localProxyPort = PortGuard.DefaultPort;
    private string _lastLogLine = "";

    private AppSettings Settings => _settingsStore.Settings;

    public MainWindow()
    {
        Title = "porkn";
        Width = 1280;
        Height = 840;
        MinWidth = 1180;
        MinHeight = 720;
        Background = Ui.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrySetIcon();
        _lastLogLine = T("Готово. Импортируй подписку/профиль или выбери сервер.", "Ready. Import a subscription/profile or select an existing server.");

        _selectedProfile = ResolveInitialProfile();
        Content = BuildRoot();
        Closing += (_, _) => Disconnect(manual: true, fromClosing: true);
        Opened += async (_, _) => await OnOpenedAsync();

        RefreshAll();
    }

    private async Task OnOpenedAsync()
    {
        await _store.RefreshSubscriptionsIfNeededAsync(Settings);
        RefreshSubscriptionsPanel();
        RefreshProfilesPanel();
        if (Settings.AutoConnectLastProfile && _selectedProfile is not null)
        {
            await ConnectProfileAsync(_selectedProfile, isSwitch: false);
        }
    }

    private void TrySetIcon()
    {
        var iconPath = Path.Combine(AppPaths.AppDirectory, "Resources", "AppIcon.ico");
        if (File.Exists(iconPath)) Icon = new WindowIcon(iconPath);
    }

    private Profile? ResolveInitialProfile()
    {
        if (Settings.LastSelectedProfileId is Guid id)
        {
            var match = _store.Profiles.FirstOrDefault(profile => profile.Id == id);
            if (match is not null) return match;
        }
        return _store.Profiles.FirstOrDefault();
    }

    private Control BuildRoot()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("400,*"),
            Background = Ui.WindowBackground
        };
        root.Children.Add(BuildSidebar());
        Grid.SetColumn(_detailHost, 1);
        root.Children.Add(_detailHost);
        return root;
    }

    private Control BuildSidebar()
    {
        var stack = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        stack.Children.Add(BuildSidebarHeader());
        stack.Children.Add(BuildSearchCard());
        stack.Children.Add(BuildProfileActions());
        stack.Children.Add(new Border
        {
            Padding = new Thickness(0, 8, 0, 10),
            Child = _profilesPanel
        });
        stack.Children.Add(BuildImportCard());
        stack.Children.Add(BuildSidebarFooter());

        return new Border
        {
            Background = Ui.SidebarBackground,
            Padding = new Thickness(24, 24, 22, 24),
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Stretch
            }
        };
    }

    private static void AddToGrid(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private Control BuildSidebarHeader()
    {
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = LoadAppIconImage() is Image appIcon
                ? appIcon
                : new TextBlock
                {
                    Text = "p",
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold,
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
        };

        var title = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text("porkn", 20, FontWeight.Bold));
        title.Children.Add(Text(T("Системный proxy Windows", "Windows System Proxy"), 12, FontWeight.Normal, Ui.SecondaryText));

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 24),
            Children = { icon, title }
        };
    }

    private Image? LoadAppIconImage()
    {
        var imagePath = Path.Combine(AppPaths.AppDirectory, "Resources", "AppIcon.png");
        if (!File.Exists(imagePath)) return null;
        return new Image
        {
            Source = new Bitmap(imagePath),
            Stretch = Stretch.UniformToFill,
            Width = 42,
            Height = 42
        };
    }

    private Control BuildSearchCard()
    {
        _searchText.Watermark = T("Поиск по имени, хосту, протоколу…", "Search name, host, protocol…");
        _searchText.TextChanged += (_, _) => RefreshProfilesPanel();
        StyleTextBox(_searchText);
        return Card(_searchText, background: Ui.InputBackground, padding: new Thickness(14, 11), radius: 18, margin: new Thickness(0, 0, 0, 18));
    }

    private Control BuildImportCard()
    {
        _importText.Watermark = T("Вставь subscription URL / VLESS / SOCKS / Trojan…", "Paste subscription URL / VLESS / SOCKS / Trojan…");
        _importText.AcceptsReturn = false;
        _importText.TextWrapping = TextWrapping.NoWrap;
        _importText.MinHeight = 42;
        _importText.MaxHeight = 42;
        StyleTextBox(_importText);

        var import = SecondaryButton(T("Импорт", "Import"));
        import.Click += async (_, _) => await ImportProfilesAsync();
        var pbk = SecondaryButton(T("PBK VPN", "PBK VPN"));
        pbk.Click += async (_, _) => await ImportPbkAsync();
        var socks = SecondaryButton(T("SOCKS", "SOCKS"));
        socks.Click += async (_, _) => await ShowSocksDialogAsync();
        var delete = SecondaryButton(T("Удалить", "Delete"), Ui.Red);
        delete.Click += (_, _) => DeleteSelectedProfile();

        var actionGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        actionGrid.Children.Add(import);
        Grid.SetColumn(pbk, 1);
        pbk.Margin = new Thickness(10, 0, 0, 0);
        actionGrid.Children.Add(pbk);
        Grid.SetRow(socks, 1);
        socks.Margin = new Thickness(0, 10, 5, 0);
        actionGrid.Children.Add(socks);
        Grid.SetColumn(delete, 1);
        Grid.SetRow(delete, 1);
        delete.Margin = new Thickness(5, 10, 0, 0);
        actionGrid.Children.Add(delete);

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Text(T("Импорт", "Import"), 12, FontWeight.Bold, Ui.SecondaryText),
                WithColumn(Text(T($"{_store.Subscriptions.Count} подписок", $"{_store.Subscriptions.Count} subscriptions"), 11, FontWeight.Normal, Ui.TertiaryText), 1)
            }
        });
        stack.Children.Add(Card(_importText, background: Ui.InputBackground, padding: new Thickness(0), radius: 14));
        stack.Children.Add(actionGrid);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(18), radius: 22, margin: new Thickness(0, 18, 0, 0));
    }

    private Control BuildImportActions()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 0, 0, 16) };
        var import = SecondaryButton(T("Импорт", "Import"));
        import.Click += async (_, _) => await ImportProfilesAsync();
        var socks = SecondaryButton(T("SOCKS", "SOCKS"));
        socks.Click += async (_, _) => await ShowSocksDialogAsync();
        var delete = SecondaryButton(T("Удалить", "Delete"), Ui.Red);
        delete.Click += (_, _) => DeleteSelectedProfile();
        grid.Children.Add(import);
        Grid.SetColumn(socks, 1);
        socks.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(delete, 2);
        delete.Margin = new Thickness(4, 0, 0, 0);
        grid.Children.Add(socks);
        grid.Children.Add(delete);
        return grid;
    }

    private Control BuildSubscriptionsSection()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(SectionLabel(T("Подписки", "Subscriptions")));
        stack.Children.Add(new ScrollViewer
        {
            Content = _subscriptionsPanel,
            MaxHeight = 138,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        return stack;
    }

    private Control BuildProfileActions()
    {
        _favoritesOnly.Content = T("Избранные", "Favorites");
        _favoritesOnly.Foreground = Ui.SecondaryText;
        _favoritesOnly.IsChecked = Settings.FavoritesOnly;
        _favoritesOnly.Checked += (_, _) => { Settings.FavoritesOnly = true; _settingsStore.Save(); RefreshProfilesPanel(); };
        _favoritesOnly.Unchecked += (_, _) => { Settings.FavoritesOnly = false; _settingsStore.Save(); RefreshProfilesPanel(); };

        _sortMode.ItemsSource = Enum.GetValues<ProfileSortMode>().Select(mode => mode.Title(Settings.Language)).ToList();
        _sortMode.SelectedIndex = (int)Settings.ProfileSortMode;
        StyleComboBox(_sortMode);
        _sortMode.SelectionChanged += (_, _) =>
        {
            if (_sortMode.SelectedIndex >= 0)
            {
                Settings.ProfileSortMode = Enum.GetValues<ProfileSortMode>()[_sortMode.SelectedIndex];
                _settingsStore.Save();
                RefreshProfilesPanel();
            }
        };

        var pingAll = SecondaryButton(T("Проверить все", "Ping All"));
        pingAll.Click += async (_, _) => await PingAllAsync();
        var fastest = SecondaryButton(T("Самый быстрый", "Auto fastest"));
        fastest.Click += (_, _) => SelectFastestProfile();

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(pingAll);
        Grid.SetColumn(fastest, 1);
        fastest.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(fastest);

        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        filters.Children.Add(_favoritesOnly);
        Grid.SetColumn(_sortMode, 1);
        _sortMode.Margin = new Thickness(14, 0, 0, 0);
        filters.Children.Add(_sortMode);

        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(SectionLabel(T("Профили", "Profiles")));
        stack.Children.Add(top);
        stack.Children.Add(filters);
        return stack;
    }

    private Control BuildSidebarFooter()
    {
        var settings = SecondaryButton(T("Настройки", "Settings"));
        settings.HorizontalAlignment = HorizontalAlignment.Stretch;
        settings.Click += (_, _) =>
        {
            _selectionKind = MainSelectionKind.Settings;
            RefreshAll();
        };

        var mode = new StackPanel { Spacing = 2 };
        mode.Children.Add(Text(T("Режим System Proxy", "System Proxy mode"), 12, FontWeight.SemiBold));
        mode.Children.Add(Text($"{LocalProxyHost}:{_localProxyPort} · sing-box", 11, FontWeight.Normal, Ui.SecondaryText, monospace: true));

        var subscriptionSummary = _store.LastRefreshSummary is not null ? RefreshSummaryText(_store.LastRefreshSummary) : T($"{_store.Subscriptions.Count} URL подписок", $"{_store.Subscriptions.Count} subscription URL(s)");

        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 4) };
        stack.Children.Add(settings);
        stack.Children.Add(Card(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Text(T("Подписки", "Subscriptions"), 11, FontWeight.Bold, Ui.SecondaryText),
                Text(subscriptionSummary, 11, FontWeight.Normal, Ui.TertiaryText)
            }
        }, Ui.SubtleCardBackground, padding: new Thickness(14, 12), radius: 16));
        stack.Children.Add(Card(mode, Ui.SubtleCardBackground, padding: new Thickness(14, 12), radius: 16));
        return stack;
    }

    private void RefreshAll()
    {
        RefreshSubscriptionsPanel();
        RefreshProfilesPanel();
        RefreshDetail();
    }

    private void RefreshSubscriptionsPanel()
    {
        _subscriptionsPanel.Children.Clear();
        if (_store.Subscriptions.Count == 0)
        {
            _subscriptionsPanel.Children.Add(Text(T("Нет subscription URL", "No subscription URL"), 11, FontWeight.Normal, Ui.SecondaryText));
            return;
        }

        foreach (var subscription in _store.Subscriptions)
        {
            _subscriptionsPanel.Children.Add(BuildSubscriptionRow(subscription));
        }

        if (_store.LastRefreshSummary is not null)
        {
            _subscriptionsPanel.Children.Add(Text(RefreshSummaryText(_store.LastRefreshSummary), 11, FontWeight.SemiBold, Ui.Green));
        }
    }

    private Control BuildSubscriptionRow(Subscription subscription)
    {
        var name = Text(subscription.Name, 12, FontWeight.SemiBold);
        var host = Text(Uri.TryCreate(subscription.Url, UriKind.Absolute, out var uri) ? uri.Host : subscription.Url, 11, FontWeight.Normal, Ui.SecondaryText);
        var info = new StackPanel { Spacing = 2, Children = { name, host } };
        if (subscription.LastRefreshAt is not null)
        {
            info.Children.Add(Text(T($"Последнее обновление: {subscription.LastRefreshAt.Value.LocalDateTime:g}", $"Last refresh: {subscription.LastRefreshAt.Value.LocalDateTime:g}"), 10, FontWeight.Normal, Ui.TertiaryText));
        }

        var refresh = TinyButton("↻");
        refresh.Click += async (_, _) => await RefreshSubscriptionAsync(subscription);
        var delete = TinyButton("×");
        delete.Click += (_, _) => { _store.Delete(subscription); RefreshAll(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { refresh, delete } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(info);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return Card(row, padding: new Thickness(12), radius: 14, margin: new Thickness(0, 0, 0, 4));
    }

    private void RefreshProfilesPanel()
    {
        _profilesPanel.Children.Clear();
        var profiles = _store.FilteredProfiles(
            _searchText.Text ?? "",
            Settings.FavoritesOnly,
            Settings.ProfileSortMode).ToList();

        if (profiles.Count == 0)
        {
            _profilesPanel.Children.Add(Card(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Text(T("Нет конфигов", "No configs"), 13, FontWeight.SemiBold),
                    Text(T("Импортируй subscription URL, VLESS, VMess, Trojan, SS или SOCKS.", "Import a subscription URL, VLESS, VMess, Trojan, SS or SOCKS."), 11, FontWeight.Normal, Ui.SecondaryText)
                }
            }, padding: new Thickness(12), radius: 16));
            return;
        }

        foreach (var profile in profiles)
        {
            _profilesPanel.Children.Add(BuildProfileRow(profile));
        }
    }

    private Control BuildProfileRow(Profile profile)
    {
        var connected = _isConnected && _activeProfile?.Id == profile.Id;
        var selected = _selectionKind == MainSelectionKind.Profile && _selectedProfile?.Id == profile.Id;
        var accent = connected ? Ui.Green : Ui.SecondaryText;
        var rowBackground = connected ? Ui.GreenSoft : selected ? Ui.CardBackground : Ui.SidebarBackground;
        var border = connected ? Ui.GreenBorder : selected ? Ui.BorderBrush : Brushes.Transparent;

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = connected ? Ui.Green : Ui.LightGlyph,
            Child = new TextBlock
            {
                Text = ProtocolInitial(profile.Protocol),
                Foreground = connected ? Brushes.White : Ui.SecondaryText,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (profile.IsFavorite) title.Children.Add(Text("★", 11, FontWeight.Bold, Ui.Yellow));
        title.Children.Add(Text(profile.Name, 13, FontWeight.SemiBold));
        if (connected)
        {
            title.Children.Add(Pill(T("Подключено", "Connected"), Ui.Green, Ui.GreenSoft));
        }

        var details = Text($"{profile.Protocol.ToUpperInvariant()} · {profile.Endpoint}", 11, FontWeight.Normal, accent, monospace: false);
        details.TextTrimming = TextTrimming.CharacterEllipsis;
        var meta = Text($"{FormatLatency(profile.LastPingMilliseconds)} · {ProfileSourceName(profile)}", 10, FontWeight.Normal, Ui.TertiaryText);

        var textStack = new StackPanel { Spacing = 3, Margin = new Thickness(12, 0, 0, 0), Children = { title, details, meta } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(icon);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var button = new Button
        {
            Content = grid,
            Background = rowBackground,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 9)
        };
        button.Click += async (_, _) => await SelectProfileAsync(profile);
        return button;
    }

    private async Task SelectProfileAsync(Profile profile)
    {
        _selectedProfile = profile;
        Settings.LastSelectedProfileId = profile.Id;
        _settingsStore.Save();
        _selectionKind = MainSelectionKind.Profile;
        _store.MarkUsed(profile);

        if (_isConnected && _activeProfile?.Id != profile.Id)
        {
            await ConnectProfileAsync(profile, isSwitch: true);
            return;
        }

        RefreshAll();
    }

    private void RefreshDetail()
    {
        _detailHost.Content = _selectionKind == MainSelectionKind.Settings
            ? BuildSettingsView()
            : BuildProfileDetailView();
    }

    private Control BuildProfileDetailView()
    {
        var stack = new StackPanel
        {
            Spacing = 20,
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        stack.Children.Add(BuildDetailHeader());

        if (_selectedProfile is null)
        {
            stack.Children.Add(BuildEmptyStateCard());
        }
        else
        {
            stack.Children.Add(BuildConnectionCard(_selectedProfile));
            stack.Children.Add(BuildMetadataCard(_selectedProfile));
            stack.Children.Add(BuildScenarioCard(_selectedProfile));
            stack.Children.Add(BuildLogsCard());
            stack.Children.Add(BuildRawConfigCard(_selectedProfile));
        }

        return new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(56, 48, 60, 52),
                Child = stack
            }
        };
    }

    private Control BuildDetailHeader()
    {
        var title = _isBusy ? (_isConnected ? T("Переключение", "Switching") : T("Подключение", "Connecting")) : _isConnected ? HealthStatusTitle() : T("Выключено", "Off");
        var color = _isBusy ? Ui.Blue : _isConnected ? HealthStatusColor() : Ui.PrimaryText;
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                Text(title, 36, FontWeight.SemiBold, color),
                Text(_lastLogLine, 14, FontWeight.Normal, Ui.SecondaryText)
            }
        };
    }

    private string HealthStatusTitle() => _activeProfile?.IsRasProfile() == true && _isConnected
        ? T("VPN подключён", "VPN connected")
        : _healthStatus.Kind switch
    {
        ProxyHealthKind.Protected or ProxyHealthKind.ProxyReachable => T("Защищено", "Protected"),
        ProxyHealthKind.Checking => T("Проверка защиты", "Checking protection"),
        ProxyHealthKind.RemoteCheckFailed or ProxyHealthKind.LocalProxyFailed => T("Подключено с предупреждением", "Connected with warning"),
        _ => T("Подключено", "Connected")
    };

    private IBrush HealthStatusColor() => _healthStatus.Kind switch
    {
        ProxyHealthKind.Protected or ProxyHealthKind.ProxyReachable => Ui.Green,
        ProxyHealthKind.Checking => Ui.Blue,
        ProxyHealthKind.RemoteCheckFailed or ProxyHealthKind.LocalProxyFailed => Ui.Warning,
        _ => Ui.PrimaryText
    };

    private Control BuildConnectionCard(Profile profile)
    {
        var connectedToThis = _isConnected && _activeProfile?.Id == profile.Id;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 6 };
        info.Children.Add(Text(profile.Name, 22, FontWeight.SemiBold));
        info.Children.Add(Text(profile.Endpoint, 14, FontWeight.Normal, Ui.SecondaryText, monospace: true));
        info.Children.Add(Text(ConnectionSubtitle(profile, connectedToThis), 12, FontWeight.Normal, Ui.TertiaryText, monospace: true));
        top.Children.Add(info);
        var badge = Pill(profile.Protocol.ToUpperInvariant(), Ui.SecondaryText, Ui.SubtleBackground, new Thickness(12, 8));
        Grid.SetColumn(badge, 1);
        top.Children.Add(badge);

        var action = PrimaryButton(connectedToThis ? T("Отключить", "Disconnect") : _isConnected ? T("Переключиться", "Switch") : T("Подключить", "Connect"), connectedToThis ? Ui.Green : Ui.Blue);
        action.IsEnabled = !_isBusy;
        action.Click += async (_, _) =>
        {
            if (connectedToThis) Disconnect(manual: true);
            else await ConnectProfileAsync(profile, isSwitch: _isConnected);
        };

        var favorite = SecondaryButton(profile.IsFavorite ? T("★ Убрать из избранного", "★ Remove from Favorites") : T("☆ Добавить в избранное", "☆ Add to Favorites"));
        favorite.Click += (_, _) => { _store.ToggleFavorite(profile); RefreshAll(); };

        var delete = SecondaryButton(T("Удалить", "Delete"), Ui.Red);
        delete.Click += (_, _) => DeleteSelectedProfile();

        var buttonRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        buttonRow.Children.Add(action);
        Grid.SetColumn(favorite, 1);
        favorite.Margin = new Thickness(12, 0, 8, 0);
        Grid.SetColumn(delete, 2);
        delete.Margin = new Thickness(0);
        buttonRow.Children.Add(favorite);
        buttonRow.Children.Add(delete);

        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(top);
        stack.Children.Add(BuildHealthRow());
        stack.Children.Add(buttonRow);
        return Card(stack, padding: new Thickness(28), radius: 26);
    }

    private string ConnectionSubtitle(Profile profile, bool connectedToThis)
    {
        if (ProfileKinds.IsRasProfile(profile))
        {
            var phonebook = profile.Query.GetValueOrDefault("phonebook_path", "managed rasphone.pbk");
            return connectedToThis ? $"Windows RAS VPN · {phonebook}" : T("Сценарий Windows RAS VPN", "Windows RAS VPN scenario");
        }

        return connectedToThis ? T($"Локальный proxy: {LocalProxyHost}:{_localProxyPort}", $"Local proxy: {LocalProxyHost}:{_localProxyPort}") : T("Сценарий System Proxy", "System Proxy scenario");
    }

    private string HealthStatusDisplayTitle() => _healthStatus.Kind switch
    {
        ProxyHealthKind.Protected => _activeProfile?.IsRasProfile() == true ? T("VPN подключён", "VPN connected") : T("Защищено", "Protected"),
        ProxyHealthKind.ProxyReachable => T("Proxy доступен", "Proxy reachable"),
        ProxyHealthKind.Checking => T("Проверка", "Checking"),
        ProxyHealthKind.RemoteCheckFailed => T("Удалённая проверка не удалась", "Remote check failed"),
        ProxyHealthKind.LocalProxyFailed => T("Локальный proxy недоступен", "Local proxy failed"),
        _ => T("Не проверено", "Not checked")
    };

    private string HealthStatusDisplayDetail() => _healthStatus.Kind switch
    {
        ProxyHealthKind.Protected when _activeProfile?.IsRasProfile() == true => T($"Windows RAS VPN entry подключён: {_activeProfile.Name}", $"Windows RAS VPN entry is connected: {_activeProfile.Name}"),
        ProxyHealthKind.Protected => T($"Proxy доступен. Remote IP: {ExtractRemoteIp(_healthStatus.Detail)}", $"Proxy is reachable. Remote IP: {ExtractRemoteIp(_healthStatus.Detail)}"),
        ProxyHealthKind.ProxyReachable => T("Локальный proxy работает, remote IP check не вернул IP.", "Local proxy works, remote IP check returned no IP."),
        ProxyHealthKind.Checking => T("Проверяем локальный proxy и удалённый маршрут…", "Verifying local proxy and remote path…"),
        ProxyHealthKind.RemoteCheckFailed or ProxyHealthKind.LocalProxyFailed => _healthStatus.Detail,
        _ => T("Подключись, чтобы проверить маршрут.", "Connect to verify the route.")
    };

    private Control BuildHealthRow()
    {
        var color = HealthStatusColor();
        var background = _healthStatus.Kind switch
        {
            ProxyHealthKind.Protected or ProxyHealthKind.ProxyReachable => Ui.GreenSoft,
            ProxyHealthKind.Checking => Ui.BlueSoft,
            ProxyHealthKind.RemoteCheckFailed or ProxyHealthKind.LocalProxyFailed => Ui.WarningSoft,
            _ => Ui.SubtleBackground
        };
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(Text(HealthStatusDisplayTitle(), 13, FontWeight.SemiBold, color));
        stack.Children.Add(Text(HealthStatusDisplayDetail(), 11, FontWeight.Normal, Ui.SecondaryText));
        return Card(stack, background, padding: new Thickness(12), radius: 14);
    }

    private Control BuildMetadataCard(Profile profile)
    {
        var ping = SecondaryButton(T("Проверить", "Check"));
        ping.Click += async (_, _) =>
        {
            var value = await _pingService.MeasureAsync(profile);
            _store.UpdatePing(profile, value);
            RefreshAll();
        };

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text(T("Параметры", "Parameters"), 16, FontWeight.SemiBold));
        stack.Children.Add(MetadataRow(T("Протокол", "Protocol"), profile.Protocol.ToUpperInvariant()));
        stack.Children.Add(MetadataRow(ProfileKinds.IsRasProfile(profile) ? T("Entry", "Entry") : T("Сервер", "Server"), profile.Endpoint, monospace: true));
        if (ProfileKinds.IsRasProfile(profile))
        {
            if (profile.Query.TryGetValue("phonebook_path", out var phonebookPath)) stack.Children.Add(MetadataRow(T("Phonebook", "Phonebook"), phonebookPath, monospace: true));
            if (profile.Query.TryGetValue("device", out var device)) stack.Children.Add(MetadataRow(T("Устройство", "Device"), device));
            if (profile.Query.TryGetValue("vpn_strategy", out var strategy)) stack.Children.Add(MetadataRow(T("Стратегия VPN", "VPN strategy"), strategy));
        }
        else
        {
            stack.Children.Add(MetadataRow(T("Ping", "Ping"), FormatLatency(profile.LastPingMilliseconds), trailing: ping));
        }
        if (profile.Query.TryGetValue("type", out var transport) || profile.Query.TryGetValue("net", out transport))
        {
            stack.Children.Add(MetadataRow(T("Транспорт", "Transport"), transport));
        }
        if (profile.Query.TryGetValue("security", out var security) || profile.Query.TryGetValue("tls", out security))
        {
            stack.Children.Add(MetadataRow(T("Безопасность", "Security"), security));
        }
        stack.Children.Add(MetadataRow(T("Источник", "Source"), ProfileSourceName(profile)));
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(24), radius: 22);
    }

    private Control BuildScenarioCard(Profile profile)
    {
        var isRas = ProfileKinds.IsRasProfile(profile);
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text(T("Сценарий подключения", "Connection scenario"), 16, FontWeight.SemiBold));
        stack.Children.Add(Pill(isRas ? T("Windows RAS VPN", "Windows RAS VPN") : T("System Proxy", "System Proxy"), Ui.PrimaryText, Ui.SubtleBackground, new Thickness(12, 7)));
        stack.Children.Add(Text(isRas
            ? T("porkn импортирует записи из rasphone.pbk, хранит управляемую копию в Application Support и подключает VPN через Windows rasdial.exe.", "porkn imports entries from rasphone.pbk, keeps a managed copy in Application Support and connects through Windows rasdial.exe.")
            : T("Windows production-режим повторяет macOS System Proxy: встроенный sing-box открывает локальный mixed proxy, а porkn направляет Windows system proxy на него.", "Windows production mode mirrors macOS System Proxy: bundled sing-box opens a local mixed proxy and porkn points Windows system proxy to it."), 12, FontWeight.Normal, Ui.SecondaryText));
        stack.Children.Add(Text(isRas
            ? T("Данные входа не извлекаются из phonebook. Если Windows уже сохранил credentials, rasdial сможет использовать их; иначе Windows может запросить данные для entry.", "Credentials are not extracted from the phonebook. If Windows has saved credentials, rasdial can reuse them; otherwise Windows may require credentials for the entry.")
            : T("Full VPN/TUN режим запланирован, но для него нужна отдельная стратегия Windows packet capture/TUN driver.", "Full VPN/TUN mode is planned, but it requires a separate Windows packet capture/TUN driver strategy."), 12, FontWeight.Normal, isRas ? Ui.SecondaryText : Ui.Warning));

        var preview = new TextBox
        {
            Text = SafeConfigPreview(profile),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MaxHeight = 260
        };
        StyleTextBox(preview, readOnly: true);
        stack.Children.Add(Text(isRas ? T("Предпросмотр PBK entry", "PBK entry preview") : T("Предпросмотр sing-box JSON", "sing-box JSON preview"), 13, FontWeight.SemiBold));
        stack.Children.Add(preview);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(24), radius: 22);
    }

    private string SafeConfigPreview(Profile profile)
    {
        if (ProfileKinds.IsRasProfile(profile)) return SensitiveRedactor.Redact(profile.RawConfig);

        try
        {
            return SensitiveRedactor.Redact(SingBoxConfigGenerator.Generate(profile, _localProxyPort, Settings.Routing));
        }
        catch (Exception ex)
        {
            return T("// Ошибка предпросмотра: ", "// Preview error: ") + ex.Message;
        }
    }

    private Control BuildLogsCard()
    {
        var logText = new TextBox
        {
            Text = string.Join(Environment.NewLine, _logLines.TakeLast(120)),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MinHeight = 130,
            MaxHeight = 220
        };
        StyleTextBox(logText, readOnly: true);
        _logTextControl = logText;
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text(T("Расширенные логи", "Advanced Logs"), 16, FontWeight.SemiBold));
        stack.Children.Add(logText);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(24), radius: 22);
    }

    private Control BuildRawConfigCard(Profile profile)
    {
        var raw = new TextBox
        {
            Text = SensitiveRedactor.Redact(profile.RawConfig),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MaxHeight = 150
        };
        StyleTextBox(raw, readOnly: true);
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text(T("Исходный конфиг", "Raw config"), 16, FontWeight.SemiBold));
        stack.Children.Add(raw);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(24), radius: 22);
    }

    private Control BuildEmptyStateCard()
    {
        var import = PrimaryButton(T("Импорт подписки", "Import Subscription"), Ui.Blue);
        import.Click += async (_, _) => await ImportProfilesAsync();
        var pbk = SecondaryButton(T("Импорт PBK VPN", "Import PBK VPN"));
        pbk.Click += async (_, _) => await ImportPbkAsync();
        var socks = SecondaryButton(T("Добавить SOCKS", "Add SOCKS"));
        socks.Click += async (_, _) => await ShowSocksDialogAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { import, pbk, socks } };
        return Card(new StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Text(T("Начни с импорта конфига", "Start by importing a config"), 22, FontWeight.SemiBold),
                Text(T("Поддерживаются subscription URL, SOCKS proxy, VLESS/Xray-compatible, Trojan, VMess и PBK VPN профили.", "Subscription URLs, SOCKS proxy, VLESS/Xray-compatible, Trojan, VMess and PBK VPN profiles are supported."), 13, FontWeight.Normal, Ui.SecondaryText),
                row
            }
        }, padding: new Thickness(28), radius: 24);
    }

    private Control BuildSettingsView()
    {
        var stack = new StackPanel { Spacing = 28, MaxWidth = 1120 };
        stack.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Text(T("Настройки", "Settings"), 38, FontWeight.SemiBold),
                Text(T("Настройки porkn применяются при следующем подключении или через Apply & Reconnect.", "porkn settings are applied on next connection or through Apply & Reconnect."), 14, FontWeight.Normal, Ui.SecondaryText)
            }
        });
        stack.Children.Add(BuildSettingsTabs());
        stack.Children.Add(_settingsTab == SettingsTab.General ? BuildGeneralSettings() : BuildRoutingSettings());
        return new ScrollViewer { Content = new Border { Padding = new Thickness(56, 48, 60, 52), Child = stack } };
    }

    private Control BuildSettingsTabs()
    {
        var general = SecondaryButton(T("Основные", "General"));
        general.Background = _settingsTab == SettingsTab.General ? Ui.BlueSoft : Ui.CardBackground;
        general.Click += (_, _) => { _settingsTab = SettingsTab.General; RefreshDetail(); };
        var routing = SecondaryButton(T("Маршрутизация", "Routing"));
        routing.Background = _settingsTab == SettingsTab.Routing ? Ui.BlueSoft : Ui.CardBackground;
        routing.Click += (_, _) => { _settingsTab = SettingsTab.Routing; RefreshDetail(); };
        routing.Margin = new Thickness(10, 0, 0, 0);
        return new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { general, WithColumn(routing, 1) } };
    }

    private Control BuildGeneralSettings()
    {
        var stack = new StackPanel { Spacing = 16 };

        var language = new ComboBox { ItemsSource = Enum.GetValues<AppLanguage>().Select(item => item.Title()).ToList(), SelectedIndex = (int)Settings.Language };
        StyleComboBox(language);
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex >= 0)
            {
                Settings.Language = Enum.GetValues<AppLanguage>()[language.SelectedIndex];
                _settingsStore.Save();
                RefreshAll();
            }
        };
        stack.Children.Add(SettingsCard(T("Интерфейс", "Interface"), T("Язык приложения и локальные параметры отображения.", "Application language and local display preferences."), language));

        var killSwitch = SettingCheckBox(T("Kill Switch", "Kill Switch"), T("Если sing-box неожиданно завершится, сохранить Windows proxy на локальном endpoint.", "If sing-box exits unexpectedly, keep Windows proxy pointed at the local endpoint."), Settings.KillSwitchEnabled, value => Settings.KillSwitchEnabled = value);
        stack.Children.Add(SettingsCard(T("Безопасность", "Security"), T("Proxy-level защита от прямого трафика при аварийном падении runtime.", "Proxy-level protection against direct traffic if runtime crashes."), killSwitch));

        var startup = new StackPanel { Spacing = 10 };
        startup.Children.Add(SettingCheckBox(T("Подключаться к последнему профилю", "Connect to last profile"), T("Автоматически подключать последний сервер при открытии приложения.", "Automatically connect the last server when porkn opens."), Settings.AutoConnectLastProfile, value => Settings.AutoConnectLastProfile = value));
        startup.Children.Add(SettingCheckBox(T("Запускать porkn при входе", "Launch porkn at login"), T("Будет доступно после добавления installer/startup integration.", "Available after installer/startup integration."), Settings.LaunchAtLogin, value => Settings.LaunchAtLogin = value, disabled: true));
        stack.Children.Add(SettingsCard(T("Запуск", "Startup"), T("Поведение приложения при старте Windows.", "Behavior when Windows starts and porkn opens."), startup));

        var subscriptions = new StackPanel { Spacing = 10 };
        var autoRefresh = new ComboBox { ItemsSource = Enum.GetValues<SubscriptionAutoRefreshInterval>().Select(item => item.Title(Settings.Language)).ToList(), SelectedIndex = (int)Settings.SubscriptionAutoRefreshInterval };
        StyleComboBox(autoRefresh);
        autoRefresh.SelectionChanged += (_, _) =>
        {
            if (autoRefresh.SelectedIndex >= 0)
            {
                Settings.SubscriptionAutoRefreshInterval = Enum.GetValues<SubscriptionAutoRefreshInterval>()[autoRefresh.SelectedIndex];
                _settingsStore.Save();
            }
        };
        subscriptions.Children.Add(autoRefresh);
        subscriptions.Children.Add(SettingCheckBox(T("Обновлять при запуске", "Refresh on app launch"), T("При открытии porkn сразу проверять subscription URL.", "Check subscription URLs when porkn opens."), Settings.RefreshSubscriptionsOnLaunch, value => Settings.RefreshSubscriptionsOnLaunch = value));
        stack.Children.Add(SettingsCard(T("Подписки", "Subscriptions"), T("Автообновление subscription URL.", "Subscription URL auto-refresh."), subscriptions));

        var update = new StackPanel { Spacing = 10 };
        var checkUpdates = SecondaryButton(T("Проверить обновления", "Check for Updates"));
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync(update);
        update.Children.Add(checkUpdates);
        stack.Children.Add(SettingsCard(T("Обновления", "Updates"), T("Проверка последнего GitHub Release.", "Check latest GitHub Release."), update));

        var core = new ComboBox { ItemsSource = new[] { "sing-box", "Xray-core" }, SelectedIndex = Settings.PreferredCore == "xray" ? 1 : 0 };
        StyleComboBox(core);
        core.SelectionChanged += (_, _) =>
        {
            Settings.PreferredCore = core.SelectedIndex == 1 ? "xray" : "sing-box";
            _settingsStore.Save();
        };
        stack.Children.Add(SettingsCard(T("Ядро", "Core"), T("Сейчас production runtime — bundled sing-box внутри приложения.", "Current production runtime is bundled sing-box inside the app."), core));
        return stack;
    }

    private Control BuildRoutingSettings()
    {
        var stack = new StackPanel { Spacing = 16 };
        var preset = new ComboBox { ItemsSource = Enum.GetValues<RoutingPreset>().Select(item => item.Title(Settings.Language)).ToList(), SelectedIndex = (int)Settings.Routing.Preset };
        StyleComboBox(preset);
        var presetDetail = Text(Settings.Routing.Preset.Detail(Settings.Language), 12, FontWeight.Normal, Ui.SecondaryText);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedIndex >= 0)
            {
                Settings.Routing.Preset = Enum.GetValues<RoutingPreset>()[preset.SelectedIndex];
                _settingsStore.Save();
                presetDetail.Text = Settings.Routing.Preset.Detail(Settings.Language);
                RefreshDetail();
            }
        };
        stack.Children.Add(SettingsCard(T("Пресет маршрутизации", "Routing preset"), T("Быстрый выбор базовой стратегии маршрутизации.", "Quickly choose the base routing strategy."), new StackPanel { Spacing = 8, Children = { preset, presetDetail } }));

        var direct = DomainEditor(T("Direct-домены", "Direct domains"), T("Идут напрямую в обход proxy", "Go directly bypassing proxy"), Settings.Routing.DirectDomainsText, value => Settings.Routing.DirectDomainsText = value);
        var proxy = DomainEditor(T("Proxy-домены", "Proxy domains"), T("Явно идут через proxy-out", "Explicitly go through proxy-out"), Settings.Routing.ProxyDomainsText, value => Settings.Routing.ProxyDomainsText = value);
        var block = DomainEditor(T("Block-домены", "Block domains"), T("Блокируются outbound block", "Blocked through the block outbound"), Settings.Routing.BlockDomainsText, value => Settings.Routing.BlockDomainsText = value);
        stack.Children.Add(SettingsCard(T("Группы доменов", "Domain groups"), T("Direct, Proxy и Block правила генерируются в sing-box route rules.", "Direct, Proxy and Block rules are generated into sing-box route rules."), new StackPanel { Spacing = 14, Children = { direct, proxy, block } }));

        var presets = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        AddPresetButton(presets, 0, "RU/SU", () => { Settings.Routing.Preset = RoutingPreset.DirectRuSu; Settings.Routing.DirectDomainsText = DomainRuleParser.AppendDomains(Settings.Routing.DirectDomainsText, ["*.ru", "*.su"]); SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 1, T("Продукты", "Products"), () => { Settings.Routing.Preset = RoutingPreset.DirectSelected; Settings.Routing.DirectDomainsText = DomainRuleParser.AppendDomains(Settings.Routing.DirectDomainsText, ["x.com", "twitter.com", "instagram.com", "facebook.com", "youtube.com", "google.com"]); SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 2, T("Обход LAN", "Bypass LAN"), () => { Settings.Routing.Preset = RoutingPreset.BypassLan; SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 3, T("Сброс", "Reset"), () => { Settings.Routing = RoutingSettings.Default; SaveSettingsAndRefresh(); });
        stack.Children.Add(SettingsCard(T("Быстрые пресеты", "Quick presets"), T("Добавь частые правила одной кнопкой.", "Add common rules with one click."), presets));

        var io = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var copy = SecondaryButton(T("Копировать JSON", "Copy JSON"));
        copy.Click += async (_, _) => await CopyRoutingJsonAsync();
        var import = SecondaryButton(T("Импорт из буфера", "Import from Clipboard"));
        import.Click += async (_, _) => await ImportRoutingJsonAsync();
        io.Children.Add(copy);
        io.Children.Add(import);
        stack.Children.Add(SettingsCard(T("Импорт / Экспорт", "Import / Export"), T("Перенос routing settings между устройствами через JSON.", "Move routing settings between devices via JSON."), io));

        var apply = PrimaryButton(_activeProfile is null ? T("Применить при следующем подключении", "Apply on Next Connect") : T("Применить и переподключить", "Apply & Reconnect"), _activeProfile is null ? Ui.SecondaryText : Ui.Green);
        apply.IsEnabled = _activeProfile is not null;
        apply.Click += async (_, _) =>
        {
            _settingsStore.Save();
            if (_activeProfile is not null) await ConnectProfileAsync(_activeProfile, isSwitch: true);
        };
        stack.Children.Add(SettingsCard(T("Применить изменения", "Apply changes"), _activeProfile is null ? T("Сейчас нет активного подключения. Новые правила применятся при следующем подключении.", "There is no active connection. New rules will apply on the next connection.") : T($"Сейчас подключён {_activeProfile.Name}. Нажми кнопку, чтобы пересобрать sing-box config.", $"Currently connected to {_activeProfile.Name}. Press the button to rebuild the sing-box config."), apply));

        var preview = new TextBox
        {
            Text = Settings.Routing.ExportJson(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MinHeight = 160
        };
        StyleTextBox(preview, readOnly: true);
        stack.Children.Add(SettingsCard(T("Предпросмотр правил", "Rules preview"), T("Так porkn добавит routing в generated sing-box config.", "This is how porkn will add routing to the generated sing-box config."), preview));
        return stack;
    }

    private Control SettingsCard(string title, string subtitle, Control content)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text(title, 16, FontWeight.SemiBold));
        stack.Children.Add(Text(subtitle, 12, FontWeight.Normal, Ui.SecondaryText));
        stack.Children.Add(content);
        return Card(stack, Ui.CardBackground, padding: new Thickness(24), radius: 22);
    }

    private Control SettingCheckBox(string title, string subtitle, bool value, Action<bool> update, bool disabled = false)
    {
        var check = new CheckBox
        {
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { Text(title, 13, FontWeight.SemiBold), Text(subtitle, 11, FontWeight.Normal, Ui.SecondaryText) }
            },
            IsChecked = value,
            IsEnabled = !disabled
        };
        check.Checked += (_, _) => { update(true); _settingsStore.Save(); };
        check.Unchecked += (_, _) => { update(false); _settingsStore.Save(); };
        return check;
    }

    private Control DomainEditor(string title, string subtitle, string value, Action<string> update)
    {
        var text = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 86 };
        StyleTextBox(text);
        text.TextChanged += (_, _) => { update(text.Text ?? ""); _settingsStore.Save(); };
        return new StackPanel { Spacing = 6, Children = { Text(title, 13, FontWeight.SemiBold), Text(subtitle, 11, FontWeight.Normal, Ui.SecondaryText), text } };
    }

    private void AddPresetButton(Grid grid, int column, string title, Action action)
    {
        var button = SecondaryButton(title);
        if (column > 0) button.Margin = new Thickness(10, 0, 0, 0);
        button.Click += (_, _) => action();
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private void SaveSettingsAndRefresh()
    {
        _settingsStore.Save();
        RefreshAll();
    }

    private async Task ImportProfilesAsync()
    {
        try
        {
            var result = await _store.ImportAsync(_importText.Text ?? "");
            _importText.Text = "";
            _selectedProfile ??= _store.Profiles.FirstOrDefault();
            AppendLog(ImportMessage(result));
            RefreshAll();
        }
        catch (Exception ex)
        {
            AppendLog(T("Импорт не удался: ", "Import failed: ") + ex.Message);
        }
    }

    private async Task ImportPbkAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("Импорт rasphone.pbk", "Import rasphone.pbk"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(T("Phonebook Windows", "Windows phonebook")) { Patterns = ["*.pbk"] },
                    FilePickerFileTypes.All
                ]
            });
            var file = files.FirstOrDefault();
            if (file is null) return;

            var path = await ResolveLocalFilePathAsync(file);
            var result = _store.ImportRasPhonebook(path);
            _selectedProfile = _store.Profiles.LastOrDefault(profile => ProfileKinds.IsRasProfile(profile)) ?? _selectedProfile;
            AppendLog(T($"Импортировано {result.ProfilesImported} Windows RAS VPN профилей из {file.Name}", $"Imported {result.ProfilesImported} Windows RAS VPN profile(s) from {file.Name}"));
            RefreshAll();
        }
        catch (Exception ex)
        {
            AppendLog(T("Импорт PBK не удался: ", "PBK import failed: ") + ex.Message);
        }
    }

    private static async Task<string> ResolveLocalFilePathAsync(IStorageFile file)
    {
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) return localPath;

        var tempPath = Path.Combine(Path.GetTempPath(), $"porkn-{Guid.NewGuid():N}-{file.Name}");
        await using var source = await file.OpenReadAsync();
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination);
        return tempPath;
    }

    private async Task RefreshSubscriptionAsync(Subscription subscription)
    {
        try
        {
            var summary = await _store.RefreshWithSummaryAsync(subscription);
            AppendLog(RefreshSummaryText(summary));
        }
        catch (Exception ex)
        {
            AppendLog(T("Обновление не удалось: ", "Refresh failed: ") + ex.Message);
        }
        RefreshAll();
    }

    private async Task ShowSocksDialogAsync()
    {
        var dialog = new AddSocksDialog(Settings.Language);
        var result = await dialog.ShowDialog<AddSocksResult?>(this);
        if (result is null) return;
        _store.AddManualSocks(result.Name, result.Host, result.Port, result.Username, result.Password);
        _selectedProfile = _store.Profiles.LastOrDefault();
        AppendLog(T($"Добавлен SOCKS профиль: {result.Name}", $"Added SOCKS profile: {result.Name}"));
        RefreshAll();
    }

    private async Task ConnectProfileAsync(Profile profile, bool isSwitch)
    {
        if (_isBusy) return;
        _isBusy = true;
        _healthStatus = ProxyHealthStatus.Checking();
        AppendLog(T($"Подготавливаю {profile.Protocol.ToUpperInvariant()} config для {profile.Endpoint}", $"Preparing {profile.Protocol.ToUpperInvariant()} config for {profile.Endpoint}"));
        RefreshAll();

        try
        {
            if (_isConnected || _singBox.IsRunning)
            {
                await StopActiveConnectionForSwitchAsync();
            }

            if (ProfileKinds.IsRasProfile(profile))
            {
                await _rasDial.ConnectAsync(profile, AppendRuntimeLog);
                _activeProfile = profile;
                _selectedProfile = profile;
                _isConnected = true;
                _isBusy = false;
                _store.MarkUsed(profile);
                _healthStatus = ProxyHealthStatus.VpnConnected(profile.Name);
                AppendLog(T($"Windows RAS VPN подключён: {profile.Name}", $"Windows RAS VPN connected: {profile.Name}"));
                RefreshAll();
                return;
            }

            _localProxyPort = PortGuard.FirstAvailablePort();
            _singBox.Start(profile, _localProxyPort, Settings.Routing, AppendRuntimeLog, OnSingBoxExited);
            _proxy.Enable(LocalProxyHost, _localProxyPort);
            _activeProfile = profile;
            _selectedProfile = profile;
            _isConnected = true;
            _isBusy = false;
            _store.MarkUsed(profile);
            AppendLog(T($"Windows system proxy включён: {LocalProxyHost}:{_localProxyPort}", $"Windows system proxy enabled: {LocalProxyHost}:{_localProxyPort}"));
            RefreshAll();

            _healthStatus = await _healthCheck.CheckAsync(LocalProxyHost, _localProxyPort);
            RefreshAll();
        }
        catch (Exception ex)
        {
            _isBusy = false;
            _isConnected = false;
            _activeProfile = null;
            try
            {
                _manualStopInProgress = true;
                if (ProfileKinds.IsRasProfile(profile))
                {
                    await _rasDial.DisconnectAsync(profile, AppendLog);
                }
                else
                {
                    _singBox.Stop();
                    _proxy.Restore();
                }
            }
            catch (Exception cleanupError)
            {
                AppendLog(T("Предупреждение cleanup: ", "Cleanup warning: ") + cleanupError.Message);
            }
            finally
            {
                _manualStopInProgress = false;
            }
            _healthStatus = ProxyHealthStatus.LocalFailed(ex.Message);
            AppendLog(T("Подключение не удалось: ", "Connect failed: ") + ex.Message);
            RefreshAll();
        }
    }

    private async Task StopActiveConnectionForSwitchAsync()
    {
        _manualStopInProgress = true;
        try
        {
            if (_activeProfile?.IsRasProfile() == true)
            {
                await _rasDial.DisconnectAsync(_activeProfile, AppendRuntimeLog);
                AppendLog(T("Предыдущий Windows RAS VPN отключён перед переключением.", "Previous Windows RAS VPN disconnected before switching."));
            }
            else
            {
                _singBox.Stop();
                _proxy.Restore();
                AppendLog(T("Предыдущий runtime остановлен. Windows system proxy восстановлен перед переключением.", "Previous runtime stopped. Windows system proxy restored before switching."));
            }
        }
        finally
        {
            _manualStopInProgress = false;
        }
    }

    private void Disconnect(bool manual, bool fromClosing = false)
    {
        try
        {
            _manualStopInProgress = true;
            if (_activeProfile?.IsRasProfile() == true)
            {
                _rasDial.DisconnectAsync(_activeProfile, AppendRuntimeLog).GetAwaiter().GetResult();
                if (!fromClosing) AppendLog(T("Отключено. Windows RAS VPN остановлен.", "Disconnected. Windows RAS VPN stopped."));
            }
            else
            {
                _singBox.Stop();
                _proxy.Restore();
                if (!fromClosing) AppendLog(T("Отключено. Windows system proxy восстановлен.", "Disconnected. Windows system proxy restored."));
            }
        }
        catch (Exception ex)
        {
            AppendLog(T("Предупреждение отключения: ", "Disconnect warning: ") + ex.Message);
        }
        finally
        {
            _manualStopInProgress = false;
            _isBusy = false;
            _isConnected = false;
            _activeProfile = null;
            _healthStatus = ProxyHealthStatus.NotChecked();
            if (!fromClosing) RefreshAll();
        }
    }

    private void OnSingBoxExited(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_manualStopInProgress) return;
            if (!_isConnected) return;
            AppendLog(T($"sing-box неожиданно завершился с кодом {exitCode}", $"sing-box exited unexpectedly with code {exitCode}"));
            _isConnected = false;
            _isBusy = false;
            _activeProfile = null;
            _healthStatus = ProxyHealthStatus.LocalFailed(T("sing-box неожиданно завершился.", "sing-box exited unexpectedly."));
            if (!Settings.KillSwitchEnabled)
            {
                try { _proxy.Restore(); }
                catch (Exception ex) { AppendLog(T("Предупреждение восстановления proxy: ", "Proxy restore warning: ") + ex.Message); }
            }
            else
            {
                AppendLog(T("Kill Switch включён: сохраняю Windows proxy на локальном endpoint.", "Kill Switch enabled: preserving Windows proxy on local endpoint."));
            }
            RefreshAll();
        });
    }

    private void DeleteSelectedProfile()
    {
        if (_selectedProfile is null) return;
        if (_activeProfile?.Id == _selectedProfile.Id) Disconnect(manual: true);
        _store.Delete(_selectedProfile);
        _selectedProfile = _store.Profiles.FirstOrDefault();
        RefreshAll();
    }

    private async Task PingAllAsync()
    {
        foreach (var profile in _store.Profiles.ToList())
        {
            var value = await _pingService.MeasureAsync(profile);
            _store.UpdatePing(profile, value);
            RefreshProfilesPanel();
        }
        AppendLog(T("Проверка всех ping завершена.", "Ping All completed."));
    }

    private void SelectFastestProfile()
    {
        var fastest = _store.SelectFastestProfile();
        if (fastest is null) return;
        _selectedProfile = fastest;
        _selectionKind = MainSelectionKind.Profile;
        RefreshAll();
    }

    private async Task CheckForUpdatesAsync(StackPanel target)
    {
        try
        {
            var result = await new UpdateCheckService().CheckAsync();
            var resultStack = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Text(UpdateCheckTitle(result), 13, FontWeight.SemiBold),
                    Text(UpdateCheckDetail(result), 11, FontWeight.Normal, Ui.SecondaryText)
                }
            };

            if (result.IsUpdateAvailable)
            {
                var install = PrimaryButton(
                    result.CanInstall ? T("Скачать и установить", "Download & Install") : T("Открыть релиз", "Open Release"),
                    result.CanInstall ? Ui.Green : Ui.Blue);
                install.Click += async (_, _) =>
                {
                    if (result.CanInstall) await InstallUpdateAsync(result, target);
                    else OpenUrl(result.ReleaseUrl);
                };
                resultStack.Children.Add(install);
            }

            target.Children.Add(Card(resultStack, result.IsUpdateAvailable ? Ui.BlueSoft : Ui.GreenSoft, padding: new Thickness(12), radius: 14));
        }
        catch (Exception ex)
        {
            target.Children.Add(Text(T("Не удалось проверить обновления: ", "Failed to check updates: ") + ex.Message, 11, FontWeight.Normal, Ui.Warning));
        }
    }

    private async Task InstallUpdateAsync(UpdateCheckResult result, StackPanel target)
    {
        try
        {
            var progress = Text(T("Подготовка обновления…", "Preparing update…"), 11, FontWeight.Normal, Ui.SecondaryText);
            target.Children.Add(progress);
            var service = new UpdateCheckService();
            await service.DownloadAndInstallAsync(result, line =>
            {
                Dispatcher.UIThread.Post(() => progress.Text = LocalizeUpdateProgress(line));
            });
            Disconnect(manual: true);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            target.Children.Add(Text(T("Не удалось установить обновление: ", "Failed to install update: ") + ex.Message, 11, FontWeight.Normal, Ui.Warning));
        }
    }

    private async Task CopyRoutingJsonAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(Settings.Routing.ExportJson());
            AppendLog(T("Routing JSON скопирован в буфер.", "Routing JSON copied to clipboard."));
        }
    }

    private async Task ImportRoutingJsonAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            AppendLog(T("Буфер обмена пуст.", "Clipboard is empty."));
            return;
        }
        try
        {
            Settings.Routing = RoutingSettings.ImportJson(text);
            SaveSettingsAndRefresh();
            AppendLog(T("Routing settings импортированы из буфера.", "Routing settings imported from clipboard."));
        }
        catch (Exception ex)
        {
            AppendLog(T("Некорректный routing JSON: ", "Invalid routing JSON: ") + ex.Message);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private string T(string ru, string en) => L10n.Text(Settings.Language, ru, en);

    private string ProfileSourceName(Profile profile)
    {
        var value = _store.SubscriptionNameFor(profile);
        return value switch
        {
            "Manual" => T("Вручную", "Manual"),
            "Subscription" => T("Подписка", "Subscription"),
            _ => value
        };
    }

    private string ImportMessage(ImportResult result) => result.Summary is not null
        ? RefreshSummaryText(result.Summary)
        : T($"Импортировано {result.ProfilesImported} профилей", $"Imported {result.ProfilesImported} profile(s)");

    private string RefreshSummaryText(SubscriptionRefreshSummary summary) =>
        T($"{summary.SubscriptionName}: +{summary.Added} / ~{summary.Updated} / -{summary.Removed}, всего {summary.Total}",
          $"{summary.SubscriptionName}: +{summary.Added} / ~{summary.Updated} / -{summary.Removed}, total {summary.Total}");

    private string UpdateCheckTitle(UpdateCheckResult result) => result.IsUpdateAvailable
        ? T($"Доступно обновление: {result.LatestVersion}", $"Update available: {result.LatestVersion}")
        : T("porkn обновлён до последней версии", "porkn is up to date");

    private string UpdateCheckDetail(UpdateCheckResult result) => result.IsUpdateAvailable
        ? T($"Установлено: {result.LocalVersion}. Последняя версия: {result.LatestVersion}." + (result.CanInstall ? $" Asset: {result.AssetName}." : " Asset для Windows не найден."),
            $"Installed: {result.LocalVersion}. Latest: {result.LatestVersion}." + (result.CanInstall ? $" Asset: {result.AssetName}." : " Windows asset was not found."))
        : T($"Установлено: {result.LocalVersion}.", $"Installed: {result.LocalVersion}.");

    private string LocalizeUpdateProgress(string line) => line switch
    {
        "Downloading update package…" => T("Скачиваю пакет обновления…", "Downloading update package…"),
        "Verifying SHA256 checksum…" => T("Проверяю SHA256 checksum…", "Verifying SHA256 checksum…"),
        "Extracting update package…" => T("Распаковываю обновление…", "Extracting update package…"),
        "Starting updater and closing porkn…" => T("Запускаю updater и закрываю porkn…", "Starting updater and closing porkn…"),
        _ => line
    };

    private static string ExtractRemoteIp(string detail)
    {
        const string marker = "Remote IP:";
        var index = detail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? detail[(index + marker.Length)..].Trim() : detail.Trim();
    }

    private void AppendRuntimeLog(string line) => AppendLog(LocalizeRuntimeLog(line));

    private string LocalizeRuntimeLog(string line)
    {
        if (line.StartsWith("sing-box exited with code ", StringComparison.OrdinalIgnoreCase))
        {
            var code = line["sing-box exited with code ".Length..].Trim();
            return T($"sing-box завершился с кодом {code}", $"sing-box exited with code {code}");
        }
        if (line.StartsWith("Connecting Windows RAS VPN: ", StringComparison.OrdinalIgnoreCase))
        {
            var entry = line["Connecting Windows RAS VPN: ".Length..].Trim();
            return T($"Подключаю Windows RAS VPN: {entry}", $"Connecting Windows RAS VPN: {entry}");
        }
        if (line.StartsWith("Disconnecting Windows RAS VPN: ", StringComparison.OrdinalIgnoreCase))
        {
            var entry = line["Disconnecting Windows RAS VPN: ".Length..].Trim();
            return T($"Отключаю Windows RAS VPN: {entry}", $"Disconnecting Windows RAS VPN: {entry}");
        }
        if (line.StartsWith("rasdial disconnect returned code ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = line["rasdial disconnect returned code ".Length..].Trim();
            return T($"rasdial вернул код отключения {rest}", $"rasdial disconnect returned code {rest}");
        }
        return line;
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }
        _lastLogLine = line;
        _logLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        if (_logLines.Count > 400) _logLines.RemoveRange(0, _logLines.Count - 400);
        if (_logTextControl is not null)
        {
            _logTextControl.Text = string.Join(Environment.NewLine, _logLines.TakeLast(120));
        }
    }

    private Control MetadataRow(string title, string value, bool monospace = false, Control? trailing = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("122,*,Auto") };
        grid.Children.Add(Text(title, 13, FontWeight.Normal, Ui.SecondaryText));
        var valueText = Text(value, 13, FontWeight.Normal, Ui.PrimaryText, monospace: monospace);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
        return grid;
    }

    private static void StyleTextBox(TextBox textBox, bool readOnly = false)
    {
        textBox.Background = readOnly ? Ui.InputReadOnlyBackground : Ui.InputBackground;
        textBox.Foreground = Ui.PrimaryText;
        textBox.BorderBrush = Brushes.Transparent;
        textBox.BorderThickness = new Thickness(0);
        textBox.Padding = new Thickness(14, 10);
    }

    private static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.Background = Ui.InputBackground;
        comboBox.Foreground = Ui.PrimaryText;
        comboBox.BorderBrush = Brushes.Transparent;
        comboBox.BorderThickness = new Thickness(0);
        comboBox.Padding = new Thickness(14, 10);
        comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private Button PrimaryButton(string text, IBrush brush) => new()
    {
        Content = text,
        Background = brush,
        Foreground = Brushes.White,
        BorderBrush = Brushes.Transparent,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18, 12),
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private Button SecondaryButton(string text, IBrush? foreground = null) => new()
    {
        Content = text,
        Foreground = foreground ?? Ui.PrimaryText,
        Background = Ui.CardBackground,
        BorderBrush = Ui.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16, 10),
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private Button TinyButton(string text) => new()
    {
        Content = text,
        Width = 28,
        Height = 28,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(10),
        Background = Ui.SubtleBackground,
        Foreground = Ui.PrimaryText,
        BorderBrush = Ui.BorderBrush,
        BorderThickness = new Thickness(1)
    };

    private static Border Card(Control child, IBrush? background = null, Thickness? padding = null, double radius = 20, Thickness? margin = null)
    {
        return new Border
        {
            Background = background ?? Ui.CardBackground,
            BorderBrush = Ui.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = padding ?? new Thickness(22),
            Margin = margin ?? new Thickness(0, 0, 0, 0),
            Child = child
        };
    }

    private static TextBlock Text(string text, double size, FontWeight weight, IBrush? color = null, bool monospace = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = color ?? Ui.PrimaryText,
            FontFamily = monospace ? FontFamily.Parse("Consolas") : FontFamily.Default,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private static TextBlock SectionLabel(string text) => Text(text, 11, FontWeight.Bold, Ui.SecondaryText);

    private static Border Pill(string text, IBrush foreground, IBrush background, Thickness? padding = null)
    {
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(99),
            Padding = padding ?? new Thickness(7, 3),
            Child = Text(text, 10, FontWeight.Bold, foreground)
        };
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static string ProtocolInitial(string protocol) => string.IsNullOrWhiteSpace(protocol) ? "P" : protocol.Trim()[0].ToString().ToUpperInvariant();

    private static string FormatLatency(int? value) => value is null ? "—" : $"{value} ms";
}

internal sealed record AddSocksResult(string Name, string Host, int Port, string? Username, string? Password);

internal sealed class AddSocksDialog : Window
{
    private readonly AppLanguage _language;
    private readonly TextBox _name = new() { Text = "SOCKS Proxy" };
    private readonly TextBox _host = new() { Text = "127.0.0.1" };
    private readonly TextBox _port = new() { Text = "1080" };
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();

    public AddSocksDialog(AppLanguage language)
    {
        _language = language;
        Title = T("Добавить SOCKS", "Add SOCKS");
        Width = 480;
        Height = 460;
        MinWidth = 460;
        Background = Ui.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
    }

    private Control Build()
    {
        StyleDialogTextBox(_name);
        StyleDialogTextBox(_host);
        StyleDialogTextBox(_port);
        StyleDialogTextBox(_username);
        StyleDialogTextBox(_password);

        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(34) };
        stack.Children.Add(new TextBlock { Text = T("Добавить SOCKS", "Add SOCKS"), FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Ui.PrimaryText });
        stack.Children.Add(Field(T("Название", "Name"), _name));
        stack.Children.Add(Field(T("Хост", "Host"), _host));
        stack.Children.Add(Field(T("Порт", "Port"), _port));
        stack.Children.Add(Field(T("Пользователь", "Username"), _username));
        stack.Children.Add(Field(T("Пароль", "Password"), _password));
        var cancel = new Button { Content = T("Отмена", "Cancel"), Padding = new Thickness(16, 9), Background = Ui.CardBackground, Foreground = Ui.PrimaryText, BorderBrush = Ui.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        cancel.Click += (_, _) => Close(null);
        var add = new Button { Content = T("Добавить", "Add"), Padding = new Thickness(18, 9), Background = Ui.Blue, Foreground = Brushes.White, BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(12) };
        add.Click += (_, _) =>
        {
            if (!int.TryParse(_port.Text, out var port)) return;
            Close(new AddSocksResult(_name.Text ?? "SOCKS Proxy", _host.Text ?? "127.0.0.1", port, EmptyToNull(_username.Text), EmptyToNull(_password.Text)));
        };
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, add } });
        return stack;
    }

    private static Control Field(string title, TextBox textBox)
    {
        return new StackPanel { Spacing = 6, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Ui.SecondaryText }, textBox } };
    }

    private string T(string ru, string en) => L10n.Text(_language, ru, en);

    private static void StyleDialogTextBox(TextBox textBox)
    {
        textBox.Background = Ui.InputBackground;
        textBox.Foreground = Ui.PrimaryText;
        textBox.BorderBrush = Brushes.Transparent;
        textBox.BorderThickness = new Thickness(0);
        textBox.Padding = new Thickness(14, 10);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class Ui
{
    public static readonly IBrush WindowBackground = Brush("#101010");
    public static readonly IBrush SidebarBackground = Brush("#161616");
    public static readonly IBrush CardBackground = Brush("#1F1F21");
    public static readonly IBrush SubtleCardBackground = Brush("#1A1A1C");
    public static readonly IBrush SubtleBackground = Brush("#2A2A2D");
    public static readonly IBrush BorderBrush = Brush("#343437");
    public static readonly IBrush InputBackground = Brush("#151516");
    public static readonly IBrush InputReadOnlyBackground = Brush("#18181A");
    public static readonly IBrush InputBorder = Brush("#303033");
    public static readonly IBrush PrimaryText = Brush("#F2F2F3");
    public static readonly IBrush SecondaryText = Brush("#B5B5B8");
    public static readonly IBrush TertiaryText = Brush("#848488");
    public static readonly IBrush Blue = Brush("#8E8E93");
    public static readonly IBrush BlueSoft = Brush("#2C2C2E");
    public static readonly IBrush Green = Brush("#4CD964");
    public static readonly IBrush GreenSoft = Brush("#243126");
    public static readonly IBrush GreenBorder = Brush("#3D5A42");
    public static readonly IBrush Red = Brush("#FF6961");
    public static readonly IBrush Warning = Brush("#F5B84B");
    public static readonly IBrush WarningSoft = Brush("#332A1C");
    public static readonly IBrush Yellow = Brush("#FFD60A");
    public static readonly IBrush LightGlyph = Brush("#303033");

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
