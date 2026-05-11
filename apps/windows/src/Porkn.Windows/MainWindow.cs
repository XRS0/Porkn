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

    private readonly StackPanel _subscriptionsPanel = new() { Spacing = 8 };
    private readonly StackPanel _profilesPanel = new() { Spacing = 6 };
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
    private string _lastLogLine = "Ready. Import a subscription/profile or select an existing server.";

    private AppSettings Settings => _settingsStore.Settings;

    public MainWindow()
    {
        Title = "porkn";
        Width = 1180;
        Height = 760;
        MinWidth = 1080;
        MinHeight = 700;
        Background = Ui.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrySetIcon();

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
            ColumnDefinitions = new ColumnDefinitions("334,*"),
            Background = Ui.WindowBackground
        };
        root.Children.Add(BuildSidebar());
        Grid.SetColumn(_detailHost, 1);
        root.Children.Add(_detailHost);
        return root;
    }

    private Control BuildSidebar()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
            Background = Ui.SidebarBackground,
            Margin = new Thickness(0),
            Width = 334
        };

        var shell = new Border
        {
            Background = Ui.SidebarBackground,
            Padding = new Thickness(18),
            Child = grid
        };

        AddToGrid(grid, BuildSidebarHeader(), 0);
        AddToGrid(grid, BuildSearchCard(), 1);
        AddToGrid(grid, BuildImportCard(), 2);
        AddToGrid(grid, BuildImportActions(), 3);
        AddToGrid(grid, BuildSubscriptionsSection(), 4);
        AddToGrid(grid, BuildProfileActions(), 5);
        AddToGrid(grid, new ScrollViewer { Content = _profilesPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 6);
        AddToGrid(grid, BuildSidebarFooter(), 7);
        return shell;
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
            Child = LoadAppIconImage() ?? new TextBlock
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
        title.Children.Add(Text("Windows System Proxy", 12, FontWeight.Normal, Ui.SecondaryText));

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 18),
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
        _searchText.Watermark = "Search name, host, protocol…";
        _searchText.TextChanged += (_, _) => RefreshProfilesPanel();
        return Card(_searchText, padding: new Thickness(12, 8), radius: 13, margin: new Thickness(0, 0, 0, 10));
    }

    private Control BuildImportCard()
    {
        _importText.Watermark = "subscription URL, VLESS, SOCKS, Trojan, VMess…";
        _importText.AcceptsReturn = true;
        _importText.TextWrapping = TextWrapping.Wrap;
        _importText.MinHeight = 86;
        _importText.MaxHeight = 116;

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Import Subscription / Config", 12, FontWeight.SemiBold));
        stack.Children.Add(_importText);
        return Card(stack, padding: new Thickness(14), radius: 18, margin: new Thickness(0, 0, 0, 10));
    }

    private Control BuildImportActions()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 0, 0, 12) };
        var import = SecondaryButton("Import");
        import.Click += async (_, _) => await ImportProfilesAsync();
        var socks = SecondaryButton("SOCKS");
        socks.Click += async (_, _) => await ShowSocksDialogAsync();
        var delete = SecondaryButton("Delete", Ui.Red);
        delete.Click += (_, _) => DeleteSelectedProfile();
        grid.Children.Add(import);
        Grid.SetColumn(socks, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(socks);
        grid.Children.Add(delete);
        return grid;
    }

    private Control BuildSubscriptionsSection()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(SectionLabel("Подписки"));
        stack.Children.Add(_subscriptionsPanel);
        return stack;
    }

    private Control BuildProfileActions()
    {
        _favoritesOnly.Content = "Favorites";
        _favoritesOnly.IsChecked = Settings.FavoritesOnly;
        _favoritesOnly.Checked += (_, _) => { Settings.FavoritesOnly = true; _settingsStore.Save(); RefreshProfilesPanel(); };
        _favoritesOnly.Unchecked += (_, _) => { Settings.FavoritesOnly = false; _settingsStore.Save(); RefreshProfilesPanel(); };

        _sortMode.ItemsSource = Enum.GetValues<ProfileSortMode>().Select(mode => mode.Title()).ToList();
        _sortMode.SelectedIndex = (int)Settings.ProfileSortMode;
        _sortMode.SelectionChanged += (_, _) =>
        {
            if (_sortMode.SelectedIndex >= 0)
            {
                Settings.ProfileSortMode = Enum.GetValues<ProfileSortMode>()[_sortMode.SelectedIndex];
                _settingsStore.Save();
                RefreshProfilesPanel();
            }
        };

        var pingAll = SecondaryButton("Ping All");
        pingAll.Click += async (_, _) => await PingAllAsync();
        var fastest = SecondaryButton("Auto fastest");
        fastest.Click += (_, _) => SelectFastestProfile();

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(pingAll);
        Grid.SetColumn(fastest, 1);
        top.Children.Add(fastest);

        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        filters.Children.Add(_favoritesOnly);
        Grid.SetColumn(_sortMode, 1);
        filters.Children.Add(_sortMode);

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(SectionLabel("Профили"));
        stack.Children.Add(top);
        stack.Children.Add(filters);
        return stack;
    }

    private Control BuildSidebarFooter()
    {
        var settings = SecondaryButton("Settings");
        settings.HorizontalAlignment = HorizontalAlignment.Stretch;
        settings.Click += (_, _) =>
        {
            _selectionKind = MainSelectionKind.Settings;
            RefreshAll();
        };

        var mode = new StackPanel { Spacing = 2 };
        mode.Children.Add(Text("System Proxy mode", 12, FontWeight.SemiBold));
        mode.Children.Add(Text($"{LocalProxyHost}:{_localProxyPort} · sing-box", 11, FontWeight.Normal, Ui.SecondaryText, monospace: true));

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        stack.Children.Add(settings);
        stack.Children.Add(Card(mode, padding: new Thickness(12, 10), radius: 16));
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
            _subscriptionsPanel.Children.Add(Text("Нет subscription URL", 11, FontWeight.Normal, Ui.SecondaryText));
            return;
        }

        foreach (var subscription in _store.Subscriptions)
        {
            _subscriptionsPanel.Children.Add(BuildSubscriptionRow(subscription));
        }

        if (_store.LastRefreshSummary is not null)
        {
            _subscriptionsPanel.Children.Add(Text(_store.LastRefreshSummary.ShortText, 11, FontWeight.SemiBold, Ui.Green));
        }
    }

    private Control BuildSubscriptionRow(Subscription subscription)
    {
        var name = Text(subscription.Name, 12, FontWeight.SemiBold);
        var host = Text(Uri.TryCreate(subscription.Url, UriKind.Absolute, out var uri) ? uri.Host : subscription.Url, 11, FontWeight.Normal, Ui.SecondaryText);
        var info = new StackPanel { Spacing = 2, Children = { name, host } };
        if (subscription.LastRefreshAt is not null)
        {
            info.Children.Add(Text($"Last refresh: {subscription.LastRefreshAt.Value.LocalDateTime:g}", 10, FontWeight.Normal, Ui.TertiaryText));
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
        return Card(row, padding: new Thickness(10), radius: 14, margin: new Thickness(0, 0, 0, 2));
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
                Spacing = 6,
                Children =
                {
                    Text("Нет конфигов", 13, FontWeight.SemiBold),
                    Text("Импортируй subscription URL, VLESS, VMess, Trojan, SS или SOCKS.", 11, FontWeight.Normal, Ui.SecondaryText)
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
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
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
            title.Children.Add(Pill("Connected", Ui.Green, Ui.GreenSoft));
        }

        var details = Text($"{profile.Protocol.ToUpperInvariant()} · {profile.Endpoint}", 11, FontWeight.Normal, accent, monospace: false);
        details.TextTrimming = TextTrimming.CharacterEllipsis;
        var meta = Text($"{FormatLatency(profile.LastPingMilliseconds)} · {_store.SubscriptionNameFor(profile)}", 10, FontWeight.Normal, Ui.TertiaryText);

        var textStack = new StackPanel { Spacing = 2, Children = { title, details, meta } };
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
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 2)
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
            MaxWidth = 980,
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
                Padding = new Thickness(30, 28),
                Child = stack
            }
        };
    }

    private Control BuildDetailHeader()
    {
        var title = _isBusy ? (_isConnected ? "Switching" : "Connecting") : _isConnected ? HealthStatusTitle() : "Off";
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

    private string HealthStatusTitle() => _healthStatus.Kind switch
    {
        ProxyHealthKind.Protected or ProxyHealthKind.ProxyReachable => "Protected",
        ProxyHealthKind.Checking => "Checking protection",
        ProxyHealthKind.RemoteCheckFailed or ProxyHealthKind.LocalProxyFailed => "Connected with warning",
        _ => "Connected"
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
        info.Children.Add(Text(connectedToThis ? $"Local proxy: {LocalProxyHost}:{_localProxyPort}" : "System Proxy scenario", 12, FontWeight.Normal, Ui.TertiaryText, monospace: true));
        top.Children.Add(info);
        var badge = Pill(profile.Protocol.ToUpperInvariant(), Ui.SecondaryText, Ui.SubtleBackground, new Thickness(12, 8));
        Grid.SetColumn(badge, 1);
        top.Children.Add(badge);

        var action = PrimaryButton(connectedToThis ? "Отключить" : _isConnected ? "Переключиться" : "Подключить", connectedToThis ? Ui.Green : Ui.Blue);
        action.IsEnabled = !_isBusy;
        action.Click += async (_, _) =>
        {
            if (connectedToThis) Disconnect(manual: true);
            else await ConnectProfileAsync(profile, isSwitch: _isConnected);
        };

        var favorite = SecondaryButton(profile.IsFavorite ? "★ Убрать из Favorites" : "☆ Добавить в Favorites");
        favorite.Click += (_, _) => { _store.ToggleFavorite(profile); RefreshAll(); };

        var delete = SecondaryButton("Удалить", Ui.Red);
        delete.Click += (_, _) => DeleteSelectedProfile();

        var buttonRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        buttonRow.Children.Add(action);
        Grid.SetColumn(favorite, 1);
        Grid.SetColumn(delete, 2);
        buttonRow.Children.Add(favorite);
        buttonRow.Children.Add(delete);

        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(top);
        stack.Children.Add(BuildHealthRow());
        stack.Children.Add(buttonRow);
        return Card(stack, padding: new Thickness(22), radius: 24);
    }

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
        stack.Children.Add(Text(_healthStatus.Title, 13, FontWeight.SemiBold, color));
        stack.Children.Add(Text(_healthStatus.Detail, 11, FontWeight.Normal, Ui.SecondaryText));
        return Card(stack, background, padding: new Thickness(12), radius: 14);
    }

    private Control BuildMetadataCard(Profile profile)
    {
        var ping = SecondaryButton("Проверить");
        ping.Click += async (_, _) =>
        {
            var value = await _pingService.MeasureAsync(profile);
            _store.UpdatePing(profile, value);
            RefreshAll();
        };

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text("Параметры", 16, FontWeight.SemiBold));
        stack.Children.Add(MetadataRow("Протокол", profile.Protocol.ToUpperInvariant()));
        stack.Children.Add(MetadataRow("Сервер", profile.Endpoint, monospace: true));
        stack.Children.Add(MetadataRow("Ping", FormatLatency(profile.LastPingMilliseconds), trailing: ping));
        if (profile.Query.TryGetValue("type", out var transport) || profile.Query.TryGetValue("net", out transport))
        {
            stack.Children.Add(MetadataRow("Transport", transport));
        }
        if (profile.Query.TryGetValue("security", out var security) || profile.Query.TryGetValue("tls", out security))
        {
            stack.Children.Add(MetadataRow("Security", security));
        }
        stack.Children.Add(MetadataRow("Subscription", _store.SubscriptionNameFor(profile)));
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(18), radius: 18);
    }

    private Control BuildScenarioCard(Profile profile)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Text("Сценарий подключения", 16, FontWeight.SemiBold));
        stack.Children.Add(Pill("System Proxy", Ui.PrimaryText, Ui.SubtleBackground, new Thickness(12, 7)));
        stack.Children.Add(Text("Windows production mode mirrors macOS System Proxy: bundled sing-box opens a local mixed proxy and porkn points Windows system proxy to it.", 12, FontWeight.Normal, Ui.SecondaryText));
        stack.Children.Add(Text("Full VPN/TUN mode is planned, but it requires a separate Windows packet capture/TUN driver strategy.", 12, FontWeight.Normal, Ui.Warning));

        var preview = new TextBox
        {
            Text = SafeConfigPreview(profile),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MaxHeight = 260
        };
        stack.Children.Add(Text("Предпросмотр sing-box JSON", 13, FontWeight.SemiBold));
        stack.Children.Add(preview);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(18), radius: 18);
    }

    private string SafeConfigPreview(Profile profile)
    {
        try
        {
            return SensitiveRedactor.Redact(SingBoxConfigGenerator.Generate(profile, _localProxyPort, Settings.Routing));
        }
        catch (Exception ex)
        {
            return "// " + ex.Message;
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
        _logTextControl = logText;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text("Advanced Logs", 16, FontWeight.SemiBold));
        stack.Children.Add(logText);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(18), radius: 18);
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
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text("Исходный конфиг", 16, FontWeight.SemiBold));
        stack.Children.Add(raw);
        return Card(stack, Ui.SubtleCardBackground, padding: new Thickness(18), radius: 18);
    }

    private Control BuildEmptyStateCard()
    {
        var import = PrimaryButton("Import Subscription", Ui.Blue);
        import.Click += async (_, _) => await ImportProfilesAsync();
        var socks = SecondaryButton("Add SOCKS");
        socks.Click += async (_, _) => await ShowSocksDialogAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { import, socks } };
        return Card(new StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Text("Начни с импорта конфига", 22, FontWeight.SemiBold),
                Text("Поддерживаются subscription URL, SOCKS proxy, VLESS/Xray-compatible, Trojan и VMess profiles.", 13, FontWeight.Normal, Ui.SecondaryText),
                row
            }
        }, padding: new Thickness(28), radius: 24);
    }

    private Control BuildSettingsView()
    {
        var stack = new StackPanel { Spacing = 22, MaxWidth = 980 };
        stack.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Text("Settings", 38, FontWeight.SemiBold),
                Text("Настройки porkn применяются при следующем подключении или через Apply & Reconnect.", 14, FontWeight.Normal, Ui.SecondaryText)
            }
        });
        stack.Children.Add(BuildSettingsTabs());
        stack.Children.Add(_settingsTab == SettingsTab.General ? BuildGeneralSettings() : BuildRoutingSettings());
        return new ScrollViewer { Content = new Border { Padding = new Thickness(30, 28), Child = stack } };
    }

    private Control BuildSettingsTabs()
    {
        var general = SecondaryButton("Основные");
        general.Background = _settingsTab == SettingsTab.General ? Ui.BlueSoft : Ui.CardBackground;
        general.Click += (_, _) => { _settingsTab = SettingsTab.General; RefreshDetail(); };
        var routing = SecondaryButton("Routing");
        routing.Background = _settingsTab == SettingsTab.Routing ? Ui.BlueSoft : Ui.CardBackground;
        routing.Click += (_, _) => { _settingsTab = SettingsTab.Routing; RefreshDetail(); };
        return new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { general, WithColumn(routing, 1) } };
    }

    private Control BuildGeneralSettings()
    {
        var stack = new StackPanel { Spacing = 16 };

        var language = new ComboBox { ItemsSource = Enum.GetValues<AppLanguage>().Select(item => item.Title()).ToList(), SelectedIndex = (int)Settings.Language };
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

        var killSwitch = SettingCheckBox("Kill Switch", T("Если sing-box неожиданно завершится, сохранить Windows proxy на локальном endpoint.", "If sing-box exits unexpectedly, keep Windows proxy pointed at the local endpoint."), Settings.KillSwitchEnabled, value => Settings.KillSwitchEnabled = value);
        stack.Children.Add(SettingsCard(T("Безопасность", "Security"), T("Proxy-level защита от прямого трафика при аварийном падении runtime.", "Proxy-level protection against direct traffic if runtime crashes."), killSwitch));

        var startup = new StackPanel { Spacing = 10 };
        startup.Children.Add(SettingCheckBox(T("Подключаться к последнему профилю", "Connect to last profile"), T("Автоматически подключать последний сервер при открытии приложения.", "Automatically connect the last server when porkn opens."), Settings.AutoConnectLastProfile, value => Settings.AutoConnectLastProfile = value));
        startup.Children.Add(SettingCheckBox(T("Запускать porkn при входе", "Launch porkn at login"), T("Будет доступно после добавления installer/startup integration.", "Available after installer/startup integration."), Settings.LaunchAtLogin, value => Settings.LaunchAtLogin = value, disabled: true));
        stack.Children.Add(SettingsCard(T("Запуск", "Startup"), T("Поведение приложения при старте Windows.", "Behavior when Windows starts and porkn opens."), startup));

        var subscriptions = new StackPanel { Spacing = 10 };
        var autoRefresh = new ComboBox { ItemsSource = Enum.GetValues<SubscriptionAutoRefreshInterval>().Select(item => item.Title()).ToList(), SelectedIndex = (int)Settings.SubscriptionAutoRefreshInterval };
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
        var checkUpdates = SecondaryButton("Check for Updates");
        checkUpdates.Click += async (_, _) => await CheckForUpdatesAsync(update);
        update.Children.Add(checkUpdates);
        stack.Children.Add(SettingsCard(T("Обновления", "Updates"), T("Проверка последнего GitHub Release.", "Check latest GitHub Release."), update));

        var core = new ComboBox { ItemsSource = new[] { "sing-box", "Xray-core" }, SelectedIndex = Settings.PreferredCore == "xray" ? 1 : 0 };
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
        var preset = new ComboBox { ItemsSource = Enum.GetValues<RoutingPreset>().Select(item => item.Title()).ToList(), SelectedIndex = (int)Settings.Routing.Preset };
        var presetDetail = Text(Settings.Routing.Preset.Detail(), 12, FontWeight.Normal, Ui.SecondaryText);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedIndex >= 0)
            {
                Settings.Routing.Preset = Enum.GetValues<RoutingPreset>()[preset.SelectedIndex];
                _settingsStore.Save();
                presetDetail.Text = Settings.Routing.Preset.Detail();
                RefreshDetail();
            }
        };
        stack.Children.Add(SettingsCard("Routing preset", "Быстрый выбор базовой стратегии маршрутизации.", new StackPanel { Spacing = 8, Children = { preset, presetDetail } }));

        var direct = DomainEditor("Direct domains", "Идут напрямую в обход proxy", Settings.Routing.DirectDomainsText, value => Settings.Routing.DirectDomainsText = value);
        var proxy = DomainEditor("Proxy domains", "Явно идут через proxy-out", Settings.Routing.ProxyDomainsText, value => Settings.Routing.ProxyDomainsText = value);
        var block = DomainEditor("Block domains", "Блокируются outbound block", Settings.Routing.BlockDomainsText, value => Settings.Routing.BlockDomainsText = value);
        stack.Children.Add(SettingsCard("Domain groups", "Direct, Proxy и Block правила генерируются в sing-box route rules.", new StackPanel { Spacing = 14, Children = { direct, proxy, block } }));

        var presets = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        AddPresetButton(presets, 0, "RU/SU", () => { Settings.Routing.Preset = RoutingPreset.DirectRuSu; Settings.Routing.DirectDomainsText = DomainRuleParser.AppendDomains(Settings.Routing.DirectDomainsText, ["*.ru", "*.su"]); SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 1, "Products", () => { Settings.Routing.Preset = RoutingPreset.DirectSelected; Settings.Routing.DirectDomainsText = DomainRuleParser.AppendDomains(Settings.Routing.DirectDomainsText, ["x.com", "twitter.com", "instagram.com", "facebook.com", "youtube.com", "google.com"]); SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 2, "Bypass LAN", () => { Settings.Routing.Preset = RoutingPreset.BypassLan; SaveSettingsAndRefresh(); });
        AddPresetButton(presets, 3, "Reset", () => { Settings.Routing = RoutingSettings.Default; SaveSettingsAndRefresh(); });
        stack.Children.Add(SettingsCard("Быстрые пресеты", "Добавь частые правила одной кнопкой.", presets));

        var io = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var copy = SecondaryButton("Copy JSON");
        copy.Click += async (_, _) => await CopyRoutingJsonAsync();
        var import = SecondaryButton("Import from Clipboard");
        import.Click += async (_, _) => await ImportRoutingJsonAsync();
        io.Children.Add(copy);
        io.Children.Add(import);
        stack.Children.Add(SettingsCard("Import / Export", "Перенос routing settings между устройствами через JSON.", io));

        var apply = PrimaryButton(_activeProfile is null ? "Apply on Next Connect" : "Apply & Reconnect", _activeProfile is null ? Ui.SecondaryText : Ui.Green);
        apply.IsEnabled = _activeProfile is not null;
        apply.Click += async (_, _) =>
        {
            _settingsStore.Save();
            if (_activeProfile is not null) await ConnectProfileAsync(_activeProfile, isSwitch: true);
        };
        stack.Children.Add(SettingsCard("Применить изменения", _activeProfile is null ? "Сейчас нет активного подключения. Новые правила применятся при следующем подключении." : $"Сейчас подключён {_activeProfile.Name}. Нажми кнопку, чтобы пересобрать sing-box config.", apply));

        var preview = new TextBox
        {
            Text = Settings.Routing.ExportJson(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("Consolas"),
            MinHeight = 160
        };
        stack.Children.Add(SettingsCard("Предпросмотр правил", "Так porkn добавит routing в generated sing-box config.", preview));
        return stack;
    }

    private Control SettingsCard(string title, string subtitle, Control content)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text(title, 16, FontWeight.SemiBold));
        stack.Children.Add(Text(subtitle, 12, FontWeight.Normal, Ui.SecondaryText));
        stack.Children.Add(content);
        return Card(stack, Ui.CardBackground, padding: new Thickness(18), radius: 18);
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
        var text = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
        text.TextChanged += (_, _) => { update(text.Text ?? ""); _settingsStore.Save(); };
        return new StackPanel { Spacing = 6, Children = { Text(title, 13, FontWeight.SemiBold), Text(subtitle, 11, FontWeight.Normal, Ui.SecondaryText), text } };
    }

    private void AddPresetButton(Grid grid, int column, string title, Action action)
    {
        var button = SecondaryButton(title);
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
            AppendLog(result.Message);
            RefreshAll();
        }
        catch (Exception ex)
        {
            AppendLog("Import failed: " + ex.Message);
        }
    }

    private async Task RefreshSubscriptionAsync(Subscription subscription)
    {
        try
        {
            var summary = await _store.RefreshWithSummaryAsync(subscription);
            AppendLog(summary.ShortText);
        }
        catch (Exception ex)
        {
            AppendLog("Refresh failed: " + ex.Message);
        }
        RefreshAll();
    }

    private async Task ShowSocksDialogAsync()
    {
        var dialog = new AddSocksDialog();
        var result = await dialog.ShowDialog<AddSocksResult?>(this);
        if (result is null) return;
        _store.AddManualSocks(result.Name, result.Host, result.Port, result.Username, result.Password);
        _selectedProfile = _store.Profiles.LastOrDefault();
        AppendLog($"Added SOCKS profile: {result.Name}");
        RefreshAll();
    }

    private async Task ConnectProfileAsync(Profile profile, bool isSwitch)
    {
        if (_isBusy) return;
        _isBusy = true;
        _healthStatus = ProxyHealthStatus.Checking();
        AppendLog($"Preparing {profile.Protocol.ToUpperInvariant()} config for {profile.Endpoint}");
        RefreshAll();

        try
        {
            if (_isConnected || _singBox.IsRunning)
            {
                _manualStopInProgress = true;
                _singBox.Stop();
                _proxy.Restore();
                _manualStopInProgress = false;
                AppendLog("Previous runtime stopped. Windows system proxy restored before switching.");
            }

            _localProxyPort = PortGuard.FirstAvailablePort();
            _singBox.Start(profile, _localProxyPort, Settings.Routing, AppendLog, OnSingBoxExited);
            _proxy.Enable(LocalProxyHost, _localProxyPort);
            _activeProfile = profile;
            _selectedProfile = profile;
            _isConnected = true;
            _isBusy = false;
            _store.MarkUsed(profile);
            AppendLog($"Windows system proxy enabled: {LocalProxyHost}:{_localProxyPort}");
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
                _singBox.Stop();
                _proxy.Restore();
            }
            catch (Exception cleanupError)
            {
                AppendLog("Cleanup warning: " + cleanupError.Message);
            }
            finally
            {
                _manualStopInProgress = false;
            }
            _healthStatus = ProxyHealthStatus.LocalFailed(ex.Message);
            AppendLog("Connect failed: " + ex.Message);
            RefreshAll();
        }
    }

    private void Disconnect(bool manual, bool fromClosing = false)
    {
        try
        {
            _manualStopInProgress = true;
            _singBox.Stop();
            _proxy.Restore();
            if (!fromClosing) AppendLog("Disconnected. Windows system proxy restored.");
        }
        catch (Exception ex)
        {
            AppendLog("Disconnect warning: " + ex.Message);
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
            AppendLog($"sing-box exited unexpectedly with code {exitCode}");
            _isConnected = false;
            _isBusy = false;
            _activeProfile = null;
            _healthStatus = ProxyHealthStatus.LocalFailed("sing-box exited unexpectedly.");
            if (!Settings.KillSwitchEnabled)
            {
                try { _proxy.Restore(); }
                catch (Exception ex) { AppendLog("Proxy restore warning: " + ex.Message); }
            }
            else
            {
                AppendLog("Kill Switch enabled: preserving Windows proxy on local endpoint.");
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
        AppendLog("Ping All completed.");
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
            target.Children.Add(Card(new StackPanel
            {
                Spacing = 4,
                Children = { Text(result.Title, 13, FontWeight.SemiBold), Text(result.Detail, 11, FontWeight.Normal, Ui.SecondaryText) }
            }, result.IsUpdateAvailable ? Ui.BlueSoft : Ui.GreenSoft, padding: new Thickness(12), radius: 14));
            if (result.IsUpdateAvailable) OpenUrl(result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            target.Children.Add(Text("Failed to check updates: " + ex.Message, 11, FontWeight.Normal, Ui.Warning));
        }
    }

    private async Task CopyRoutingJsonAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(Settings.Routing.ExportJson());
            AppendLog("Routing JSON copied to clipboard.");
        }
    }

    private async Task ImportRoutingJsonAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            AppendLog("Clipboard is empty.");
            return;
        }
        try
        {
            Settings.Routing = RoutingSettings.ImportJson(text);
            SaveSettingsAndRefresh();
            AppendLog("Routing settings imported from clipboard.");
        }
        catch (Exception ex)
        {
            AppendLog("Invalid routing JSON: " + ex.Message);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private string T(string ru, string en) => L10n.Text(Settings.Language, ru, en);

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
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 8),
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
        Background = Ui.CardBackground,
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
            Padding = padding ?? new Thickness(20),
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
    private readonly TextBox _name = new() { Text = "SOCKS Proxy" };
    private readonly TextBox _host = new() { Text = "127.0.0.1" };
    private readonly TextBox _port = new() { Text = "1080" };
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();

    public AddSocksDialog()
    {
        Title = "Add SOCKS";
        Width = 420;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
    }

    private Control Build()
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock { Text = "Add SOCKS", FontSize = 22, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(Field("Name", _name));
        stack.Children.Add(Field("Host", _host));
        stack.Children.Add(Field("Port", _port));
        stack.Children.Add(Field("Username", _username));
        stack.Children.Add(Field("Password", _password));
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8) };
        cancel.Click += (_, _) => Close(null);
        var add = new Button { Content = "Add", Padding = new Thickness(14, 8), Background = Ui.Blue, Foreground = Brushes.White };
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
        return new StackPanel { Spacing = 4, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, textBox } };
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class Ui
{
    public static readonly IBrush WindowBackground = Brush("#F6F6F8");
    public static readonly IBrush SidebarBackground = Brush("#EFF0F4");
    public static readonly IBrush CardBackground = Brush("#FFFFFF");
    public static readonly IBrush SubtleCardBackground = Brush("#FCFCFD");
    public static readonly IBrush SubtleBackground = Brush("#F6F7FA");
    public static readonly IBrush BorderBrush = Brush("#DEE0E6");
    public static readonly IBrush PrimaryText = Brush("#1D1D1F");
    public static readonly IBrush SecondaryText = Brush("#696970");
    public static readonly IBrush TertiaryText = Brush("#91919A");
    public static readonly IBrush Blue = Brush("#0070F5");
    public static readonly IBrush BlueSoft = Brush("#E8F2FF");
    public static readonly IBrush Green = Brush("#249C52");
    public static readonly IBrush GreenSoft = Brush("#E7F7ED");
    public static readonly IBrush GreenBorder = Brush("#B2E2C1");
    public static readonly IBrush Red = Brush("#CC3B3B");
    public static readonly IBrush Warning = Brush("#D68B15");
    public static readonly IBrush WarningSoft = Brush("#FFF5E4");
    public static readonly IBrush Yellow = Brush("#D8A600");
    public static readonly IBrush LightGlyph = Brush("#E0E2E8");

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
