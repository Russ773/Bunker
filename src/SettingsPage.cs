using System.Diagnostics;
using System.Drawing;

namespace Bunker;

/// <summary>Settings with a vertical submenu: Servers, General, Updates, About.</summary>
public class SettingsPage : Panel
{
    private readonly Core _core;
    private readonly Dictionary<string, (NavButton Button, Control Panel)> _sections = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(14, 0, 0, 0) };

    // Servers
    private readonly DarkGrid _grid = new();
    private readonly Button _add = Theme.PrimaryButton("Add server");
    private readonly Button _edit = Theme.GhostButton("Edit");
    private readonly Button _remove = Theme.GhostButton("Remove");
    private readonly Button _use = Theme.GhostButton("Connect");

    // Updates
    private readonly Button _check = Theme.PrimaryButton("Check for updates");
    private readonly ProgressBar _progress = new() { Width = 420, Height = 6, Visible = false, Location = new Point(0, 132) };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Theme.Muted, Location = new Point(0, 148) };

    public SettingsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        var menu = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Panel, Padding = new Padding(0, 8, 0, 8) };
        menu.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, menu.Width - 1, menu.Height - 1); };

        var defs = new (string Key, string Glyph, string Title, Func<Control> Build)[]
        {
            ("servers", "", "Servers", BuildServers),
            ("general", "", "General", BuildGeneral),
            ("updates", "", "Updates", BuildUpdates),
            ("about", "", "About", BuildAbout),
        };
        foreach (var d in defs)
        {
            var panel = d.Build();
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
            _content.Controls.Add(panel);
            var key = d.Key;
            var button = new NavButton(d.Glyph, d.Title) { Height = 42 };
            button.Click += (_, _) => ShowSection(key);
            _sections[key] = (button, panel);
        }
        foreach (var d in defs.Reverse()) menu.Controls.Add(_sections[d.Key].Button);

        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(_content);
        body.Controls.Add(menu);

        var page = new Page("Settings", "Your servers, startup behaviour and updates", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        _core.ServerChanged += RefreshServers;
        _core.StateChanged += RefreshServers;
        RefreshServers();
        ShowSection("servers");
    }

    public void ShowSection(string key)
    {
        foreach (var (k, s) in _sections) { s.Panel.Visible = k == key; s.Button.Selected = k == key; }
    }

    private static Card Wrap(string title, Control inner) => Card.Titled(title, inner);

    // ---------------------------------------------------------------- Servers

    private Control BuildServers()
    {
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("Address", "Address");
        _grid.Columns.Add("Status", "Status");
        _grid.Columns[0].FillWeight = 120;
        _grid.Columns[1].FillWeight = 110;
        _grid.Columns[2].FillWeight = 70;
        _grid.Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Panel };
        foreach (var b in new[] { _add, _edit, _remove, _use }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); bar.Controls.Add(b); }

        var hint = new Label
        {
            Text = "Add as many servers as you like. Click the server name in the sidebar to switch between them.",
            Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0),
        };

        var inner = new Panel { BackColor = Theme.Panel };
        inner.Controls.Add(_grid);
        inner.Controls.Add(bar);
        inner.Controls.Add(hint);

        _add.Click += (_, _) => AddServer();
        _edit.Click += (_, _) => EditServer();
        _remove.Click += async (_, _) => await RemoveServerAsync();
        _use.Click += async (_, _) => { if (SelectedServer() is { } p) await _core.SwitchServerAsync(p.Id); };
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditServer(); };
        return Wrap("Servers", inner);
    }

    private ServerProfile? SelectedServer() =>
        _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ServerProfile : null;

    private void RefreshServers()
    {
        var selectedId = SelectedServer()?.Id;
        var activeId = _core.Settings.ActiveServer?.Id;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var s in _core.Settings.Servers)
        {
            var status = s.Id != activeId ? "" : _core.State == ConnState.Connected ? "Active - connected" : "Active";
            var i = _grid.Rows.Add(s.DisplayName, $"{s.Host}:{s.Port}", status);
            _grid.Rows[i].Tag = s;
            if (s.Id == activeId) _grid.Rows[i].Cells[2].Style.ForeColor = _core.State == ConnState.Connected ? Theme.Ok : Theme.Accent;
            if (s.Id == (selectedId ?? activeId)) _grid.Rows[i].Selected = true;
        }
        _grid.ResumeLayout();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var p = SelectedServer();
        _edit.Enabled = _remove.Enabled = p is not null;
        var isActive = p is not null && p.Id == _core.Settings.ActiveServer?.Id;
        _use.Enabled = p is not null && !(isActive && _core.State != ConnState.Disconnected);
        _use.Text = isActive ? "Connect" : "Switch to";
    }

    private void AddServer()
    {
        using var f = new OnboardingForm(null, firstRun: false);
        if (f.ShowDialog(FindForm()) == DialogResult.OK)
            _core.SaveServer(f.Result!, makeActive: _core.Settings.Servers.Count == 0);
    }

    private void EditServer()
    {
        if (SelectedServer() is not { } p) return;
        using var f = new OnboardingForm(p, firstRun: false);
        if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
        _core.SaveServer(f.Result!);
        if (p.Id == _core.Settings.ActiveServer?.Id && _core.State == ConnState.Connected)
            _core.Log.Add("Server details changed. Disconnect and connect again to use them.", LogKind.Info);
    }

    private async Task RemoveServerAsync()
    {
        if (SelectedServer() is not { } p) return;
        var answer = MessageBox.Show(FindForm(),
            $"Remove \"{p.DisplayName}\" from Bunker?\n\nThis only forgets the saved connection details. Your game server is not affected.",
            "Remove server", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) await _core.DeleteServerAsync(p.Id);
    }

    // ---------------------------------------------------------------- General

    private Control BuildGeneral()
    {
        var inner = new Panel { BackColor = Theme.Panel };

        // Theme: dark or light. The colours are chosen once when Bunker starts, so a change needs a restart.
        inner.Controls.Add(new Label { Text = "Theme", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 6) });
        var dark = new RadioButton { Text = "Dark", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(0, 28), Checked = !Theme.IsLight };
        var light = new RadioButton { Text = "Light", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(90, 28), Checked = Theme.IsLight };
        void ThemeChanged(bool toLight)
        {
            if (_core.Settings.LightTheme == toLight) return;
            _core.Settings.LightTheme = toLight;
            _core.Settings.Save();
            if (toLight == Theme.IsLight) return;
            var restart = MessageBox.Show(FindForm(), "Restart Bunker now to switch theme?\n\nChoose No to switch the next time Bunker starts.",
                "Theme changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (restart == DialogResult.Yes) Application.Restart();
        }
        dark.CheckedChanged += (_, _) => { if (dark.Checked) ThemeChanged(false); };
        light.CheckedChanged += (_, _) => { if (light.Checked) ThemeChanged(true); };
        inner.Controls.Add(dark);
        inner.Controls.Add(light);

        inner.Controls.Add(Check("Start Bunker maximised", _core.Settings.StartMaximised, 72, v => _core.Settings.StartMaximised = v));
        inner.Controls.Add(Check("Connect to the active server when Bunker starts", _core.Settings.AutoConnect, 106, v => _core.Settings.AutoConnect = v));
        inner.Controls.Add(new Label { Text = "Theme, maximised and connect settings take effect the next time Bunker starts.", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 146) });
        return Wrap("General", inner);
    }

    private CheckBox Check(string text, bool value, int top, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(0, top), Checked = value };
        c.CheckedChanged += (_, _) => { set(c.Checked); _core.Settings.Save(); };
        return c;
    }

    // ---------------------------------------------------------------- Updates

    private Control BuildUpdates()
    {
        var inner = new Panel { BackColor = Theme.Panel };
        inner.Controls.Add(new Label { Text = $"Current version: {Updater.VersionText}", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(0, 6) });
        inner.Controls.Add(Check("Check for updates when Bunker starts (you are asked before anything installs)", _core.Settings.CheckForUpdatesOnLaunch, 36, v => _core.Settings.CheckForUpdatesOnLaunch = v));
        _check.Location = new Point(0, 76);
        inner.Controls.Add(_check);
        inner.Controls.Add(_progress);
        inner.Controls.Add(_result);
        _check.Click += async (_, _) => await CheckForUpdateAsync();
        return Wrap("Updates", inner);
    }

    private async Task CheckForUpdateAsync()
    {
        _check.Enabled = false;
        _result.ForeColor = Theme.Muted;
        _result.Text = "Checking...";
        try
        {
            var update = await Updater.CheckAsync();
            if (update is null) { _result.ForeColor = Theme.Ok; _result.Text = "You're up to date."; return; }

            var answer = MessageBox.Show(FindForm(),
                $"Version {update.Version} is available (you have {Updater.CurrentVersion}).\n\nInstall now? Bunker will restart.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) { _result.Text = $"Version {update.Version} is available."; return; }

            _progress.Visible = true;
            _result.Text = "Downloading...";
            await Updater.DownloadAndLaunchAsync(update, new Progress<int>(p => _progress.Value = p));
            Application.Exit();
        }
        catch (Exception e)
        {
            _result.ForeColor = Theme.Err;
            _result.Text = "Update check failed: " + e.Message;
        }
        finally { _check.Enabled = true; }
    }

    // ---------------------------------------------------------------- About

    private Control BuildAbout()
    {
        var inner = new Panel { BackColor = Theme.Panel };
        if (SplashForm.LoadLogo() is { } logo)
            inner.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(72, 72), Location = new Point(0, 6), BackColor = Theme.Panel });
        inner.Controls.Add(new Label { Text = "Bunker", Font = new Font("Segoe UI Semibold", 16f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(86, 8) });
        inner.Controls.Add(new Label { Text = Updater.VersionText, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(88, 42) });
        inner.Controls.Add(new Label
        {
            Text = "Manage your Rust game server from your desktop: watch the console, see who is online and run commands without being in the game.",
            ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(520, 0), Location = new Point(0, 96),
        });

        var github = Link("Project page on GitHub", $"https://github.com/{Updater.Repo}", 150);
        var releases = Link("All releases and downloads", $"https://github.com/{Updater.Repo}/releases", 176);
        inner.Controls.Add(github);
        inner.Controls.Add(releases);

        var folder = Theme.GhostButton("Open settings folder");
        folder.Location = new Point(0, 216);
        folder.Click += (_, _) => { try { Directory.CreateDirectory(AppSettings.Folder); Process.Start(new ProcessStartInfo(AppSettings.Folder) { UseShellExecute = true }); } catch { } };
        inner.Controls.Add(folder);
        return Wrap("About", inner);
    }

    private static LinkLabel Link(string text, string url, int top)
    {
        var l = new LinkLabel
        {
            Text = text, AutoSize = true, Location = new Point(0, top), LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover,
            VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
        };
        l.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
        return l;
    }
}
