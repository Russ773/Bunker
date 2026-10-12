using System.Drawing;

namespace Bunker;

/// <summary>Left-hand navigation plus the connection status and Connect/Disconnect button.</summary>
public class Sidebar : Panel
{
    private readonly Core _core;
    private readonly Dictionary<string, NavButton> _items = new();
    private readonly Label _serverName = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = false, Height = 22, Dock = DockStyle.Top };
    private readonly Label _serverAddr = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Height = 20, Dock = DockStyle.Top };
    private readonly Label _status = new() { Font = Theme.Small, AutoSize = false, Height = 26, Dock = DockStyle.Top, Padding = new Padding(0, 4, 0, 0) };
    private readonly Button _connect = Theme.PrimaryButton("Connect");

    public event Action<string>? Navigated;
    public event Action? ConnectClicked;

    public Sidebar(Core core)
    {
        _core = core;
        Width = 236;
        Dock = DockStyle.Left;
        BackColor = Theme.Panel;
        Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height); };

        // Connection block (bottom). Docked-top children stack with the last added on top, so add in reverse.
        var conn = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(20, 14, 20, 16), BackColor = Theme.Panel };
        _connect.Dock = DockStyle.Bottom;
        _connect.Height = 38;
        conn.Controls.Add(_connect);
        conn.Controls.Add(_status);
        conn.Controls.Add(_serverAddr);
        conn.Controls.Add(_serverName);
        conn.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 14, 0, conn.Width - 14, 0); };

        // Nav items
        var nav = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        var defs = new (string Key, string Glyph, string Text)[]
        {
            ("dashboard", "", "Dashboard"), ("console", "", "Console"), ("players", "", "Players"),
            ("layout", "", "Layout"), ("settings", "", "Settings"),
        };
        foreach (var d in defs.Reverse())
        {
            var b = new NavButton(d.Glyph, d.Text);
            var key = d.Key;
            b.Click += (_, _) => Navigated?.Invoke(key);
            _items[key] = b;
            nav.Controls.Add(b);
        }

        // Brand (top)
        var brand = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Panel };
        if (Theme.AppIcon is { } icon)
            brand.Controls.Add(new PictureBox { Image = SplashForm.LoadLogo() ?? icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(38, 38), Location = new Point(20, 20), BackColor = Theme.Panel });
        brand.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Text, AutoSize = true, Location = new Point(66, 18) });
        brand.Controls.Add(new Label { Text = Updater.VersionText, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(68, 42) });

        Controls.Add(nav);
        Controls.Add(brand);
        Controls.Add(conn);

        _connect.Click += (_, _) => ConnectClicked?.Invoke();
        _core.StateChanged += RefreshState;
        _core.ServerChanged += RefreshState;
        RefreshState();
    }

    public void Select(string key)
    {
        foreach (var (k, b) in _items) b.Selected = k == key;
    }

    private void RefreshState()
    {
        var s = _core.Settings.Server;
        _serverName.Text = s?.DisplayName ?? "No server yet";
        _serverAddr.Text = s is null ? "Click Connect to add one" : $"{s.Host}:{s.Port}";
        _status.Text = "●  " + _core.StatusText;
        _status.ForeColor = _core.State switch
        {
            ConnState.Connected => Theme.Ok,
            ConnState.Connecting => Theme.Warn,
            _ => Theme.Muted,
        };
        _connect.Text = _core.State == ConnState.Connected ? "Disconnect" : "Connect";
        _connect.Enabled = _core.State != ConnState.Connecting;
    }
}
