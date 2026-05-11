namespace Porkn.Windows;

internal sealed class MainForm : Form
{
    private const int LocalProxyPort = 2080;
    private readonly ProfileStore _store = new();
    private readonly SingBoxProcessManager _singBox = new();
    private readonly WindowsProxyManager _proxy = new();

    private readonly ListBox _profiles = new() { Dock = DockStyle.Fill };
    private readonly TextBox _importText = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110, Dock = DockStyle.Top };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Text = "Off", AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.DimGray };
    private readonly Label _details = new() { Text = "Import a subscription/profile, select a server and connect.", AutoSize = true };
    private readonly Button _connect = new() { Text = "Connect", Width = 110 };
    private readonly Button _disconnect = new() { Text = "Disconnect", Width = 110, Enabled = false };
    private readonly Button _import = new() { Text = "Import", Width = 110 };
    private readonly Button _delete = new() { Text = "Delete", Width = 110 };

    public MainForm()
    {
        Text = "porkn for Windows";
        MinimumSize = new Size(980, 640);
        Font = new Font("Segoe UI", 10);
        BuildLayout();
        WireEvents();
        ReloadProfiles();
    }

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 360,
            FixedPanel = FixedPanel.Panel1
        };

        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            Padding = new Padding(12)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "porkn Windows",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        sidebar.Controls.Add(title, 0, 0);
        sidebar.Controls.Add(_importText, 0, 1);

        var importButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        importButtons.Controls.Add(_import);
        importButtons.Controls.Add(_delete);
        sidebar.Controls.Add(importButtons, 0, 2);
        sidebar.Controls.Add(_profiles, 0, 3);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            Padding = new Padding(18)
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(_status, 0, 0);
        content.Controls.Add(_details, 0, 1);

        var actionButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 18, 0, 18) };
        actionButtons.Controls.Add(_connect);
        actionButtons.Controls.Add(_disconnect);
        content.Controls.Add(actionButtons, 0, 2);

        var logGroup = new GroupBox { Text = "Runtime logs", Dock = DockStyle.Fill };
        logGroup.Controls.Add(_log);
        content.Controls.Add(logGroup, 0, 3);

        split.Panel1.Controls.Add(sidebar);
        split.Panel2.Controls.Add(content);
        Controls.Add(split);
    }

    private void WireEvents()
    {
        _import.Click += async (_, _) => await ImportProfilesAsync();
        _delete.Click += (_, _) => DeleteSelected();
        _connect.Click += (_, _) => ConnectSelected();
        _disconnect.Click += (_, _) => Disconnect();
        _profiles.SelectedIndexChanged += (_, _) => UpdateDetails();
        FormClosing += (_, _) => Disconnect();
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
            ReloadProfiles();
            AppendLog($"Imported {profiles.Count} profile(s)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConnectSelected()
    {
        if (_profiles.SelectedItem is not Profile profile)
        {
            MessageBox.Show("Select a profile first.", "porkn", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _status.Text = "Connecting";
            _status.ForeColor = Color.RoyalBlue;
            AppendLog($"Preparing {profile.Protocol.ToUpperInvariant()} config for {profile.Endpoint}");
            _singBox.Start(profile, LocalProxyPort, AppendLog);
            _proxy.Enable("127.0.0.1", LocalProxyPort);
            _status.Text = "Protected";
            _status.ForeColor = Color.ForestGreen;
            _details.Text = $"{profile.Name}\n{profile.Protocol.ToUpperInvariant()} · {profile.Endpoint}\nSystem proxy: 127.0.0.1:{LocalProxyPort}";
            _connect.Enabled = false;
            _disconnect.Enabled = true;
            AppendLog($"Windows system proxy enabled: 127.0.0.1:{LocalProxyPort}");
        }
        catch (Exception ex)
        {
            _status.Text = "Failed";
            _status.ForeColor = Color.Firebrick;
            AppendLog(ex.Message);
            MessageBox.Show(ex.Message, "Connect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Disconnect();
        }
    }

    private void Disconnect()
    {
        try
        {
            _singBox.Stop();
            _proxy.Restore();
            AppendLog("Disconnected. Windows system proxy restored.");
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect warning: {ex.Message}");
        }
        finally
        {
            _status.Text = "Off";
            _status.ForeColor = Color.DimGray;
            _connect.Enabled = true;
            _disconnect.Enabled = false;
            UpdateDetails();
        }
    }

    private void DeleteSelected()
    {
        if (_profiles.SelectedItem is not Profile profile) return;
        _store.Delete(profile);
        ReloadProfiles();
    }

    private void ReloadProfiles()
    {
        _profiles.Items.Clear();
        foreach (var profile in _store.Profiles) _profiles.Items.Add(profile);
        if (_profiles.Items.Count > 0) _profiles.SelectedIndex = 0;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_disconnect.Enabled) return;
        if (_profiles.SelectedItem is Profile profile)
        {
            _details.Text = $"{profile.Name}\n{profile.Protocol.ToUpperInvariant()} · {profile.Endpoint}";
        }
        else
        {
            _details.Text = "Import a subscription/profile, select a server and connect.";
        }
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }

        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }
}
