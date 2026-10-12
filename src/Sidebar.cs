using System.Drawing;

namespace Bunker;

/// <summary>Left-hand navigation plus the server, its status and the Connect/Disconnect button.</summary>
public class Sidebar : Panel
{
    private readonly Core _core;
    private readonly Dictionary<string, NavButton> _items = new();
    private readonly Label _serverName = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Height = 22, Dock = DockStyle.Top };
    private readonly Label _serverAddr = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Height = 20, Dock = DockStyle.Top };
    private readonly Label _status = new() { Font = Theme.Small, AutoSize = false, Height = 26, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 0) };
    private readonly Button _connect = Theme.PrimaryButton("Connect");

    public event Action<string>? Navigated;
    public event Action? ConnectClicked;
    public event Action<string>? ServerSwitchRequested;

    public Sidebar(Core core)
    {
        _core = core;
        Width = 236;
        Dock = DockStyle.Left;
        BackColor = Theme.Panel;
        Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height); };

        // Server + connection block (very bottom). Docked-top children stack with the last added on top, so add in reverse.
        var conn = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(20, 14, 20, 16), BackColor = Theme.Panel };
        _connect.Dock = DockStyle.Bottom;
        _connect.Height = 38;
        conn.Controls.Add(_connect);
        conn.Controls.Add(_status);
        conn.Controls.Add(_serverAddr);
        conn.Controls.Add(_serverName);
        conn.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 14, 0, conn.Width - 14, 0); };
        foreach (var l in new[] { _serverName, _serverAddr }) { l.Click += (_, _) => ShowServerMenu(); }

        // Main navigation (top) and the less-used items (just above the server block)
        var nav = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        AddItems(nav, ("dashboard", "", "Dashboard"), ("console", "", "Console"), ("players", "", "Players"), ("tools", "", "Tools"));

        var lower = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Panel, Padding = new Padding(0, 8, 0, 6) };
        AddItems(lower, ("layout", "", "Layout"), ("settings", "", "Settings"));
        lower.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 14, 0, lower.Width - 14, 0); };

        // Brand (top)
        var brand = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Panel };
        if (Theme.AppIcon is { } icon)
            brand.Controls.Add(new PictureBox { Image = SplashForm.LoadLogo() ?? icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(38, 38), Location = new Point(20, 20), BackColor = Theme.Panel });
        brand.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Text, AutoSize = true, Location = new Point(66, 18) });
        brand.Controls.Add(new Label { Text = Updater.VersionText, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(68, 42) });

        Controls.Add(nav);
        Controls.Add(brand);
        Controls.Add(lower);
        Controls.Add(conn);

        _connect.Click += (_, _) => ConnectClicked?.Invoke();
        _core.StateChanged += RefreshState;
        _core.ServerChanged += RefreshState;
        RefreshState();
    }

    /// <summary>Adds nav buttons in the given order (Top-docked children stack last-added-first, so add reversed).</summary>
    private void AddItems(Panel host, params (string Key, string Glyph, string Text)[] defs)
    {
        foreach (var d in defs.Reverse())
        {
            var b = new NavButton(d.Glyph, d.Text);
            var key = d.Key;
            b.Click += (_, _) => Navigated?.Invoke(key);
            _items[key] = b;
            host.Controls.Add(b);
        }
    }

    public void Select(string key)
    {
        foreach (var (k, b) in _items) b.Selected = k == key;
    }

    private void ShowServerMenu()
    {
        var servers = _core.Settings.Servers;
        if (servers.Count == 0) return;
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        foreach (var s in servers)
        {
            var id = s.Id;
            var item = new ToolStripMenuItem(s.DisplayName) { Checked = id == _core.Settings.ActiveServer?.Id, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) => { if (id != _core.Settings.ActiveServer?.Id) ServerSwitchRequested?.Invoke(id); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var manage = new ToolStripMenuItem("Manage servers...") { BackColor = Theme.Input, ForeColor = Theme.Muted };
        manage.Click += (_, _) => Navigated?.Invoke("settings:servers");
        menu.Items.Add(manage);
        menu.Show(_serverName, new Point(0, _serverName.Height));
    }

    private void RefreshState()
    {
        var s = _core.Settings.ActiveServer;
        var many = _core.Settings.Servers.Count > 1;
        _serverName.Text = s is null ? "No server yet" : s.DisplayName + (many ? "  ▾" : "");
        _serverAddr.Text = s is null ? "Click Connect to add one" : $"{s.Host}:{s.Port}";
        _serverName.Cursor = _serverAddr.Cursor = _core.Settings.Servers.Count > 0 ? Cursors.Hand : Cursors.Default;
        _status.Text = "●  " + _core.StatusText;
        _status.ForeColor = _core.State switch
        {
            ConnState.Connected => Theme.Ok,
            ConnState.Connecting => Theme.Warn,
            _ => Theme.Muted,
        };
        Tips.Set(_serverName, s?.DisplayName + (many ? "\nClick to switch server" : ""));
        Tips.Set(_serverAddr, _serverAddr.Text);
        _connect.Text = _core.State == ConnState.Connected ? "Disconnect" : "Connect";
        _connect.Enabled = _core.State != ConnState.Connecting;
    }
}
