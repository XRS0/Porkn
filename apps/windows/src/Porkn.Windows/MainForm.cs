using System.Drawing.Drawing2D;

namespace Porkn.Windows;

internal sealed class MainForm : Form
{
    private const int LocalProxyPort = 2080;
    private const string LocalProxyHost = "127.0.0.1";

    private readonly ProfileStore _store = new();
    private readonly SingBoxProcessManager _singBox = new();
    private readonly WindowsProxyManager _proxy = new();

    private readonly TextBox _searchText = new();
    private readonly TextBox _importText = new();
    private readonly ListBox _profiles = new();
    private readonly TextBox _log = new();

    private readonly Label _statusTitle = new();
    private readonly Label _statusSubtitle = new();
    private readonly Label _connectionName = new();
    private readonly Label _connectionEndpoint = new();
    private readonly Label _localProxyLabel = new();
    private readonly Label _protocolBadge = new();
    private readonly Label _healthTitle = new();
    private readonly Label _healthDetail = new();
    private readonly Label _metadataProtocol = new();
    private readonly Label _metadataServer = new();
    private readonly Label _metadataUser = new();
    private readonly Label _metadataMode = new();

    private readonly RoundedPanel _healthPanel = new();
    private readonly RoundedPanel _protocolBadgePanel = new();
    private readonly RoundedButton _primaryAction = new();
    private readonly RoundedButton _importButton = new();
    private readonly RoundedButton _deleteButton = new();

    private Image? _appIconImage;
    private Profile? _activeProfile;
    private bool _isConnected;
    private bool _isConnecting;
    private bool _suppressSelectionSwitch;
    private string _lastLogLine = "Ready. Import a subscription/profile or select an existing server.";

    public MainForm()
    {
        Text = "porkn";
        MinimumSize = new Size(1120, 720);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiTheme.Font(10);
        BackColor = UiTheme.WindowBackground;

        TryLoadAppIcon();
        BuildLayout();
        WireEvents();
        ReloadProfiles();
        SetStatus("Off", UiTheme.PrimaryText, _lastLogLine);
        ActiveControl = null;
    }

    private void TryLoadAppIcon()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var iconPath = Path.Combine(baseDirectory, "Resources", "AppIcon.ico");
        var imagePath = Path.Combine(baseDirectory, "Resources", "AppIcon.png");

        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AppIcon.ico");
        }

        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        if (!File.Exists(imagePath))
        {
            imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AppIcon.png");
        }

        if (File.Exists(imagePath))
        {
            _appIconImage = Image.FromFile(imagePath);
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.WindowBackground,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 334));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildContent(), 1, 0);
        Controls.Add(root);

        ResumeLayout(true);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 8,
            ColumnCount = 1,
            Padding = new Padding(18, 18, 16, 18),
            BackColor = UiTheme.SidebarBackground,
            Margin = Padding.Empty
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));

        sidebar.Controls.Add(BuildSidebarHeader(), 0, 0);
        sidebar.Controls.Add(BuildSearchCard(), 0, 1);
        sidebar.Controls.Add(BuildImportCard(), 0, 2);
        sidebar.Controls.Add(BuildSidebarActions(), 0, 3);
        sidebar.Controls.Add(SectionLabel("Профили"), 0, 4);
        sidebar.Controls.Add(BuildProfileList(), 0, 5);
        sidebar.Controls.Add(BuildSidebarFooter(), 0, 7);

        return sidebar;
    }

    private Control BuildSidebarHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 18),
            BackColor = Color.Transparent
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconCard = new RoundedPanel
        {
            Width = 42,
            Height = 42,
            CornerRadius = 12,
            FillColor = Color.Black,
            BorderColor = Color.FromArgb(35, 35, 38),
            Margin = new Padding(0, 0, 8, 0)
        };

        if (_appIconImage is not null)
        {
            iconCard.Controls.Add(new PictureBox
            {
                Image = _appIconImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            });
        }
        else
        {
            iconCard.Controls.Add(new Label
            {
                Text = "p",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = UiTheme.Font(18, FontStyle.Bold)
            });
        }

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.Controls.Add(new Label
        {
            Text = "porkn",
            AutoSize = true,
            ForeColor = UiTheme.PrimaryText,
            Font = UiTheme.Font(20, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 0)
        }, 0, 0);
        titleStack.Controls.Add(new Label
        {
            Text = "Windows System Proxy",
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            Font = UiTheme.Font(9),
            Margin = new Padding(1, 0, 0, 0)
        }, 0, 1);

        header.Controls.Add(iconCard, 0, 0);
        header.Controls.Add(titleStack, 1, 0);
        return header;
    }

    private Control BuildSearchCard()
    {
        _searchText.BorderStyle = BorderStyle.None;
        _searchText.PlaceholderText = "Search name, host, protocol…";
        _searchText.BackColor = UiTheme.CardBackground;
        _searchText.ForeColor = UiTheme.PrimaryText;
        _searchText.Font = UiTheme.Font(10);
        _searchText.Dock = DockStyle.Fill;
        _searchText.Margin = Padding.Empty;

        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 13,
            FillColor = UiTheme.CardBackground,
            BorderColor = UiTheme.Border,
            Padding = new Padding(13, 11, 13, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Controls.Add(_searchText);
        return card;
    }

    private Control BuildImportCard()
    {
        _importText.Multiline = true;
        _importText.AcceptsReturn = true;
        _importText.ScrollBars = ScrollBars.Vertical;
        _importText.BorderStyle = BorderStyle.None;
        _importText.PlaceholderText = "subscription URL, VLESS, SOCKS or Trojan…";
        _importText.BackColor = UiTheme.SubtleCardBackground;
        _importText.ForeColor = UiTheme.PrimaryText;
        _importText.Font = UiTheme.Font(9.5f);
        _importText.Dock = DockStyle.Fill;
        _importText.Margin = Padding.Empty;

        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 18,
            FillColor = UiTheme.CardBackground,
            BorderColor = UiTheme.Border,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Import Subscription / Config",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.PrimaryText,
            Font = UiTheme.Font(9.5f, FontStyle.Bold),
            Margin = Padding.Empty
        }, 0, 0);

        var inputShell = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            FillColor = UiTheme.SubtleCardBackground,
            BorderColor = Color.FromArgb(232, 233, 238),
            Padding = new Padding(10, 8, 10, 8),
            Margin = Padding.Empty
        };
        inputShell.Controls.Add(_importText);
        layout.Controls.Add(inputShell, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSidebarActions()
    {
        _importButton.Text = "Import";
        _importButton.Font = UiTheme.Font(9.5f, FontStyle.Bold);
        _importButton.Height = 34;
        _importButton.Dock = DockStyle.Fill;
        _importButton.Margin = new Padding(0, 0, 7, 8);
        _importButton.CornerRadius = 12;

        ConfigureSecondaryButton(_deleteButton, "Delete", UiTheme.Red);
        _deleteButton.Height = 34;
        _deleteButton.Dock = DockStyle.Fill;
        _deleteButton.Margin = new Padding(7, 0, 0, 8);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.Controls.Add(_importButton, 0, 0);
        actions.Controls.Add(_deleteButton, 1, 0);
        return actions;
    }

    private Control BuildProfileList()
    {
        _profiles.Dock = DockStyle.Fill;
        _profiles.BorderStyle = BorderStyle.None;
        _profiles.BackColor = UiTheme.SidebarBackground;
        _profiles.ForeColor = UiTheme.PrimaryText;
        _profiles.DrawMode = DrawMode.OwnerDrawVariable;
        _profiles.IntegralHeight = false;
        _profiles.ItemHeight = 76;
        _profiles.Margin = new Padding(0, 8, 0, 0);
        _profiles.TabStop = false;
        return _profiles;
    }

    private Control BuildSidebarFooter()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 16,
            FillColor = Color.FromArgb(247, 248, 251),
            BorderColor = UiTheme.Border,
            Padding = new Padding(12, 10, 12, 10),
            Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(new Label
        {
            Text = "System Proxy mode",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.PrimaryText,
            Font = UiTheme.Font(9.5f, FontStyle.Bold),
            Margin = Padding.Empty
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = $"{LocalProxyHost}:{LocalProxyPort} · sing-box",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.SecondaryText,
            Font = UiTheme.Mono(8.5f),
            Margin = Padding.Empty
        }, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildContent()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.WindowBackground,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(30, 28, 30, 28),
            BackColor = UiTheme.WindowBackground,
            Margin = Padding.Empty
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        stack.Controls.Add(BuildHeader(), 0, 0);
        stack.Controls.Add(BuildConnectionCard(), 0, 1);
        stack.Controls.Add(BuildMetadataCard(), 0, 2);
        stack.Controls.Add(BuildLogsCard(), 0, 3);
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 20)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _statusTitle.Text = "Off";
        _statusTitle.AutoSize = true;
        _statusTitle.ForeColor = UiTheme.PrimaryText;
        _statusTitle.Font = UiTheme.Font(34, FontStyle.Bold);
        _statusTitle.Margin = Padding.Empty;

        _statusSubtitle.Text = _lastLogLine;
        _statusSubtitle.AutoSize = false;
        _statusSubtitle.Dock = DockStyle.Top;
        _statusSubtitle.Height = 44;
        _statusSubtitle.ForeColor = UiTheme.SecondaryText;
        _statusSubtitle.Font = UiTheme.Font(10.5f);
        _statusSubtitle.Margin = new Padding(2, 6, 0, 0);
        _statusSubtitle.AutoEllipsis = true;

        header.Controls.Add(_statusTitle, 0, 0);
        header.Controls.Add(_statusSubtitle, 0, 1);
        return header;
    }

    private Control BuildConnectionCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 250,
            CornerRadius = 24,
            FillColor = UiTheme.CardBackground,
            BorderColor = UiTheme.Border,
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 18)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));

        var nameStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        nameStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        nameStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        nameStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        _connectionName.Text = "Выбери профиль";
        _connectionName.Dock = DockStyle.Fill;
        _connectionName.ForeColor = UiTheme.PrimaryText;
        _connectionName.Font = UiTheme.Font(18, FontStyle.Bold);
        _connectionName.AutoEllipsis = true;
        _connectionName.Margin = Padding.Empty;

        _connectionEndpoint.Text = "Import a subscription/profile, select a server and connect.";
        _connectionEndpoint.Dock = DockStyle.Fill;
        _connectionEndpoint.ForeColor = UiTheme.SecondaryText;
        _connectionEndpoint.Font = UiTheme.Mono(9.5f);
        _connectionEndpoint.AutoEllipsis = true;
        _connectionEndpoint.Margin = Padding.Empty;

        _localProxyLabel.Text = "System Proxy scenario";
        _localProxyLabel.Dock = DockStyle.Fill;
        _localProxyLabel.ForeColor = UiTheme.TertiaryText;
        _localProxyLabel.Font = UiTheme.Mono(8.7f);
        _localProxyLabel.AutoEllipsis = true;
        _localProxyLabel.Margin = Padding.Empty;

        nameStack.Controls.Add(_connectionName, 0, 0);
        nameStack.Controls.Add(_connectionEndpoint, 0, 1);
        nameStack.Controls.Add(_localProxyLabel, 0, 2);

        _protocolBadgePanel.Dock = DockStyle.Top;
        _protocolBadgePanel.Height = 38;
        _protocolBadgePanel.CornerRadius = 18;
        _protocolBadgePanel.FillColor = Color.FromArgb(246, 247, 250);
        _protocolBadgePanel.BorderColor = Color.FromArgb(232, 233, 238);
        _protocolBadgePanel.Padding = new Padding(12, 8, 12, 8);
        _protocolBadgePanel.Margin = new Padding(12, 0, 0, 0);
        _protocolBadge.Text = "PROFILE";
        _protocolBadge.Dock = DockStyle.Fill;
        _protocolBadge.TextAlign = ContentAlignment.MiddleCenter;
        _protocolBadge.ForeColor = UiTheme.SecondaryText;
        _protocolBadge.Font = UiTheme.Font(9, FontStyle.Bold);
        _protocolBadge.Margin = Padding.Empty;
        _protocolBadgePanel.Controls.Add(_protocolBadge);

        top.Controls.Add(nameStack, 0, 0);
        top.Controls.Add(_protocolBadgePanel, 1, 0);
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(BuildHealthBox(), 0, 1);

        _primaryAction.Text = "Подключить";
        _primaryAction.Dock = DockStyle.Top;
        _primaryAction.Height = 52;
        _primaryAction.Font = UiTheme.Font(13, FontStyle.Bold);
        _primaryAction.CornerRadius = 16;
        _primaryAction.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_primaryAction, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildHealthBox()
    {
        _healthPanel.Dock = DockStyle.Fill;
        _healthPanel.CornerRadius = 16;
        _healthPanel.FillColor = Color.FromArgb(246, 247, 250);
        _healthPanel.BorderColor = Color.FromArgb(232, 233, 238);
        _healthPanel.Padding = new Padding(13, 10, 13, 10);
        _healthPanel.Margin = new Padding(0, 6, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _healthTitle.Text = "Not checked";
        _healthTitle.Dock = DockStyle.Fill;
        _healthTitle.ForeColor = UiTheme.SecondaryText;
        _healthTitle.Font = UiTheme.Font(10.5f, FontStyle.Bold);
        _healthTitle.Margin = Padding.Empty;

        _healthDetail.Text = "Connect to verify the local proxy path.";
        _healthDetail.Dock = DockStyle.Fill;
        _healthDetail.ForeColor = UiTheme.SecondaryText;
        _healthDetail.Font = UiTheme.Font(9.2f);
        _healthDetail.AutoEllipsis = true;
        _healthDetail.Margin = Padding.Empty;

        layout.Controls.Add(_healthTitle, 0, 0);
        layout.Controls.Add(_healthDetail, 0, 1);
        _healthPanel.Controls.Add(layout);
        return _healthPanel;
    }

    private Control BuildMetadataCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 188,
            CornerRadius = 20,
            FillColor = Color.FromArgb(252, 252, 253),
            BorderColor = UiTheme.Border,
            Padding = new Padding(20, 17, 20, 17),
            Margin = new Padding(0, 0, 0, 18)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));

        var title = new Label
        {
            Text = "Параметры",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.PrimaryText,
            Font = UiTheme.Font(12, FontStyle.Bold),
            Margin = Padding.Empty
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        AddMetadataRow(layout, 1, "Протокол", _metadataProtocol);
        AddMetadataRow(layout, 2, "Сервер", _metadataServer, mono: true);
        AddMetadataRow(layout, 3, "User", _metadataUser);
        AddMetadataRow(layout, 4, "Mode", _metadataMode);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildLogsCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 248,
            CornerRadius = 20,
            FillColor = Color.FromArgb(252, 252, 253),
            BorderColor = UiTheme.Border,
            Padding = new Padding(20, 17, 20, 17),
            Margin = new Padding(0, 0, 0, 22)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Advanced Logs",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.PrimaryText,
            Font = UiTheme.Font(12, FontStyle.Bold),
            Margin = Padding.Empty
        }, 0, 0);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Color.FromArgb(246, 247, 250);
        _log.ForeColor = UiTheme.SecondaryText;
        _log.Font = UiTheme.Mono(9);
        _log.Dock = DockStyle.Fill;
        _log.Margin = Padding.Empty;
        _log.TabStop = false;

        var logShell = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14,
            FillColor = Color.FromArgb(246, 247, 250),
            BorderColor = Color.FromArgb(232, 233, 238),
            Padding = new Padding(12, 10, 12, 10),
            Margin = Padding.Empty
        };
        logShell.Controls.Add(_log);
        layout.Controls.Add(logShell, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = true,
        ForeColor = UiTheme.SecondaryText,
        Font = UiTheme.Font(9, FontStyle.Bold),
        Margin = new Padding(2, 6, 0, 0)
    };

    private static void AddMetadataRow(TableLayoutPanel layout, int row, string title, Label value, bool mono = false)
    {
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.SecondaryText,
            Font = UiTheme.Font(10),
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);

        value.Text = "—";
        value.Dock = DockStyle.Fill;
        value.ForeColor = UiTheme.PrimaryText;
        value.Font = mono ? UiTheme.Mono(9.5f) : UiTheme.Font(10);
        value.Margin = Padding.Empty;
        value.AutoEllipsis = true;
        value.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(value, 1, row);
    }

    private static void ConfigureSecondaryButton(RoundedButton button, string text, Color? textColor = null)
    {
        button.Text = text;
        button.Font = UiTheme.Font(9.5f, FontStyle.Bold);
        button.CornerRadius = 12;
        button.NormalColor = UiTheme.CardBackground;
        button.HoverColor = Color.FromArgb(247, 248, 251);
        button.PressedColor = Color.FromArgb(238, 240, 245);
        button.DisabledColor = Color.FromArgb(236, 237, 242);
        button.TextColor = textColor ?? UiTheme.PrimaryText;
        button.DisabledTextColor = UiTheme.TertiaryText;
        button.BorderColor = UiTheme.Border;
        button.DrawButtonBorder = true;
    }

    private void WireEvents()
    {
        _importButton.Click += async (_, _) => await ImportProfilesAsync();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _primaryAction.Click += (_, _) => ToggleSelectedProfile();
        _searchText.TextChanged += (_, _) => ReloadProfiles(preferredId: SelectedProfile?.Id ?? _activeProfile?.Id);
        _profiles.SelectedIndexChanged += (_, _) => HandleProfileSelectionChanged();
        _profiles.MeasureItem += (_, e) => e.ItemHeight = 76;
        _profiles.DrawItem += DrawProfileItem;
        FormClosing += (_, _) => Disconnect(fromClosing: true);
    }

    private Profile? SelectedProfile => _profiles.SelectedItem as Profile;

    private IEnumerable<Profile> FilteredProfiles()
    {
        var query = _searchText.Text.Trim();
        var profiles = _store.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Endpoint, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(query))
        {
            return profiles;
        }

        return profiles.Where(profile =>
            profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || profile.Host.Contains(query, StringComparison.OrdinalIgnoreCase)
            || profile.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
            || profile.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ImportProfilesAsync()
    {
        try
        {
            var profiles = await ConfigParser.ImportAsync(_importText.Text);
            if (profiles.Count == 0)
            {
                MessageBox.Show("No supported profiles found.", "porkn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _store.Upsert(profiles);
            _importText.Clear();
            ReloadProfiles(preferredId: profiles[0].Id);
            AppendLog($"Imported {profiles.Count} profile(s)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleSelectedProfile()
    {
        if (SelectedProfile is not Profile profile)
        {
            MessageBox.Show("Select a profile first.", "porkn", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_isConnected && _activeProfile?.Id == profile.Id)
        {
            Disconnect();
            return;
        }

        ConnectProfile(profile, isSwitch: _isConnected);
    }

    private void ConnectProfile(Profile profile, bool isSwitch = false)
    {
        if (_isConnecting) return;

        try
        {
            _isConnecting = true;
            SetStatus(isSwitch ? "Switching" : "Connecting", UiTheme.Blue, $"Preparing {profile.Protocol.ToUpperInvariant()} config for {profile.Endpoint}");
            SetHealth("Checking protection", "Starting sing-box and enabling Windows system proxy…", UiTheme.Blue);
            UpdatePrimaryButton();
            AppendLog($"Preparing {profile.Protocol.ToUpperInvariant()} config for {profile.Endpoint}");

            if (_isConnected)
            {
                _singBox.Stop();
                _proxy.Restore();
                AppendLog("Previous runtime stopped. Windows system proxy restored before switching.");
            }

            _singBox.Start(profile, LocalProxyPort, AppendLog);
            _proxy.Enable(LocalProxyHost, LocalProxyPort);

            _activeProfile = profile;
            _isConnected = true;
            _isConnecting = false;

            SetStatus("Protected", UiTheme.Green, $"Windows system proxy enabled: {LocalProxyHost}:{LocalProxyPort}");
            SetHealth("Protected", $"Local mixed proxy is active at {LocalProxyHost}:{LocalProxyPort}.", UiTheme.Green);
            AppendLog($"Windows system proxy enabled: {LocalProxyHost}:{LocalProxyPort}");
        }
        catch (Exception ex)
        {
            _isConnecting = false;
            _isConnected = false;
            _activeProfile = null;

            try
            {
                _singBox.Stop();
                _proxy.Restore();
            }
            catch (Exception cleanupError)
            {
                AppendLog($"Cleanup warning: {cleanupError.Message}");
            }

            SetStatus("Failed", UiTheme.Red, ex.Message);
            SetHealth("Local proxy failed", ex.Message, UiTheme.Red);
            AppendLog(ex.Message);
            MessageBox.Show(ex.Message, "Connect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateDetails();
            _profiles.Invalidate();
        }
    }

    private void Disconnect(bool fromClosing = false)
    {
        var hadRuntime = _isConnected || _isConnecting || _activeProfile is not null;

        try
        {
            _singBox.Stop();
            _proxy.Restore();
            if (hadRuntime && !fromClosing)
            {
                AppendLog("Disconnected. Windows system proxy restored.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect warning: {ex.Message}");
        }
        finally
        {
            _isConnecting = false;
            _isConnected = false;
            _activeProfile = null;
            SetStatus("Off", UiTheme.PrimaryText, hadRuntime ? "Windows system proxy restored." : _lastLogLine);
            SetHealth("Not checked", "Connect to verify the local proxy path.", UiTheme.SecondaryText);
            UpdateDetails();
            _profiles.Invalidate();
        }
    }

    private void DeleteSelected()
    {
        if (SelectedProfile is not Profile profile) return;

        if (_activeProfile?.Id == profile.Id)
        {
            Disconnect();
        }

        _store.Delete(profile);
        ReloadProfiles();
        AppendLog($"Deleted profile: {profile.Name}");
    }

    private void ReloadProfiles(Guid? preferredId = null)
    {
        var currentId = preferredId ?? SelectedProfile?.Id ?? _activeProfile?.Id;
        var profiles = FilteredProfiles().ToList();

        _suppressSelectionSwitch = true;
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var profile in profiles)
        {
            _profiles.Items.Add(profile);
        }
        _profiles.EndUpdate();

        if (_profiles.Items.Count > 0)
        {
            var index = 0;
            if (currentId is Guid id)
            {
                var match = profiles.FindIndex(profile => profile.Id == id);
                if (match >= 0) index = match;
            }

            _profiles.SelectedIndex = index;
        }
        _suppressSelectionSwitch = false;
        UpdateDetails();
    }

    private void HandleProfileSelectionChanged()
    {
        if (_suppressSelectionSwitch)
        {
            UpdateDetails();
            return;
        }

        if (_isConnected && SelectedProfile is Profile profile && _activeProfile?.Id != profile.Id)
        {
            ConnectProfile(profile, isSwitch: true);
            return;
        }

        UpdateDetails();
    }

    private void UpdateDetails()
    {
        var profile = SelectedProfile;

        if (profile is null)
        {
            _connectionName.Text = "Начни с импорта конфига";
            _connectionEndpoint.Text = "Subscription URL, VLESS, SOCKS and Trojan are supported.";
            _localProxyLabel.Text = "Логи появятся после запуска sing-box.";
            _protocolBadge.Text = "PROFILE";
            _metadataProtocol.Text = "—";
            _metadataServer.Text = "—";
            _metadataUser.Text = "—";
            _metadataMode.Text = "System Proxy";
            _primaryAction.Enabled = false;
            _deleteButton.Enabled = false;
            UpdatePrimaryButton();
            return;
        }

        _connectionName.Text = profile.Name;
        _connectionEndpoint.Text = profile.Endpoint;
        _localProxyLabel.Text = _activeProfile?.Id == profile.Id && _isConnected
            ? $"Local proxy: {LocalProxyHost}:{LocalProxyPort}"
            : "System Proxy scenario";
        _protocolBadge.Text = profile.Protocol.ToUpperInvariant();
        _metadataProtocol.Text = profile.Protocol.ToUpperInvariant();
        _metadataServer.Text = profile.Endpoint;
        _metadataUser.Text = string.IsNullOrWhiteSpace(profile.Username) ? "—" : profile.Username;
        _metadataMode.Text = $"Windows System Proxy · {LocalProxyHost}:{LocalProxyPort}";
        _primaryAction.Enabled = !_isConnecting;
        _deleteButton.Enabled = !_isConnecting;

        if (!_isConnected && !_isConnecting)
        {
            SetHealth("Not checked", "Connect to verify the local proxy path.", UiTheme.SecondaryText);
        }
        else if (_activeProfile?.Id != profile.Id)
        {
            SetHealth("Another server active", $"Connected to {_activeProfile?.Name ?? "another profile"}. Selecting this row will switch.", UiTheme.Blue);
        }

        UpdatePrimaryButton();
    }

    private void UpdatePrimaryButton()
    {
        if (SelectedProfile is null)
        {
            _primaryAction.Text = "Подключить";
            SetPrimaryButtonColor(UiTheme.Blue, UiTheme.BlueHover, UiTheme.BluePressed);
            return;
        }

        if (_isConnecting)
        {
            _primaryAction.Text = "Подключаю…";
            SetPrimaryButtonColor(UiTheme.Blue, UiTheme.BlueHover, UiTheme.BluePressed);
            return;
        }

        if (_isConnected && _activeProfile?.Id == SelectedProfile.Id)
        {
            _primaryAction.Text = "Отключить";
            SetPrimaryButtonColor(UiTheme.Green, UiTheme.GreenHover, UiTheme.GreenPressed);
            return;
        }

        _primaryAction.Text = _isConnected ? "Переключиться" : "Подключить";
        SetPrimaryButtonColor(UiTheme.Blue, UiTheme.BlueHover, UiTheme.BluePressed);
    }

    private void SetPrimaryButtonColor(Color normal, Color hover, Color pressed)
    {
        _primaryAction.NormalColor = normal;
        _primaryAction.HoverColor = hover;
        _primaryAction.PressedColor = pressed;
        _primaryAction.TextColor = Color.White;
        _primaryAction.Invalidate();
    }

    private void SetStatus(string title, Color color, string subtitle)
    {
        _statusTitle.Text = title;
        _statusTitle.ForeColor = color;
        _statusSubtitle.Text = subtitle;
        _statusSubtitle.ForeColor = UiTheme.SecondaryText;
    }

    private void SetHealth(string title, string detail, Color color)
    {
        var fill = Blend(color, Color.White, 0.90f);
        _healthPanel.FillColor = fill;
        _healthPanel.BorderColor = Blend(color, Color.White, 0.76f);
        _healthTitle.Text = title;
        _healthTitle.ForeColor = color;
        _healthDetail.Text = detail;
        _healthDetail.ForeColor = UiTheme.SecondaryText;
        _healthPanel.Invalidate();
    }

    private static Color Blend(Color foreground, Color background, float backgroundAmount)
    {
        var foregroundAmount = 1f - backgroundAmount;
        return Color.FromArgb(
            (int)(foreground.R * foregroundAmount + background.R * backgroundAmount),
            (int)(foreground.G * foregroundAmount + background.G * backgroundAmount),
            (int)(foreground.B * foregroundAmount + background.B * backgroundAmount));
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }

        _lastLogLine = line;
        _statusSubtitle.Text = line;
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }

    private void DrawProfileItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _profiles.Items.Count) return;
        if (_profiles.Items[e.Index] is not Profile profile) return;

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(UiTheme.SidebarBackground);
        graphics.FillRectangle(background, e.Bounds);

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var connected = _isConnected && _activeProfile?.Id == profile.Id;
        var bounds = e.Bounds;
        bounds.Inflate(-2, -4);
        bounds.Width -= 2;
        bounds.Height -= 2;

        var fillColor = connected
            ? Color.FromArgb(231, 247, 237)
            : selected
                ? UiTheme.CardBackground
                : UiTheme.SidebarBackground;
        var borderColor = connected
            ? Color.FromArgb(178, 226, 193)
            : selected
                ? UiTheme.Border
                : UiTheme.SidebarBackground;

        using (var path = DrawingExtensions.RoundedRect(bounds, 16))
        using (var fill = new SolidBrush(fillColor))
        using (var border = new Pen(borderColor, 1f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        var iconRect = new Rectangle(bounds.Left + 12, bounds.Top + 18, 30, 30);
        using (var iconBrush = new SolidBrush(connected ? UiTheme.Green : Color.FromArgb(224, 226, 232)))
        {
            graphics.FillEllipse(iconBrush, iconRect);
        }

        TextRenderer.DrawText(
            graphics,
            ProtocolInitial(profile.Protocol),
            UiTheme.Font(8.5f, FontStyle.Bold),
            iconRect,
            connected ? Color.White : UiTheme.SecondaryText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (connected)
        {
            var dotRect = new Rectangle(iconRect.Right - 6, iconRect.Bottom - 5, 9, 9);
            using var dotBrush = new SolidBrush(UiTheme.Green);
            using var dotBorder = new Pen(Color.White, 2f);
            graphics.FillEllipse(dotBrush, dotRect);
            graphics.DrawEllipse(dotBorder, dotRect);
        }

        var nameRect = new Rectangle(bounds.Left + 52, bounds.Top + 11, bounds.Width - 64, 22);
        var endpointRect = new Rectangle(bounds.Left + 52, bounds.Top + 34, bounds.Width - 64, 18);
        var metaRect = new Rectangle(bounds.Left + 52, bounds.Top + 53, bounds.Width - 64, 16);

        TextRenderer.DrawText(
            graphics,
            profile.Name,
            UiTheme.Font(9.6f, FontStyle.Bold),
            nameRect,
            UiTheme.PrimaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            graphics,
            $"{profile.Protocol.ToUpperInvariant()} · {profile.Endpoint}",
            UiTheme.Font(8.4f),
            endpointRect,
            connected ? UiTheme.Green : UiTheme.SecondaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            graphics,
            connected ? "Connected" : "System Proxy",
            UiTheme.Font(7.8f),
            metaRect,
            connected ? UiTheme.Green : UiTheme.TertiaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static string ProtocolInitial(string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol)) return "P";
        return protocol.Trim()[0].ToString().ToUpperInvariant();
    }
}
