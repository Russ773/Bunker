using System.Drawing;

namespace Bunker;

/// <summary>Left-hand navigation plus the server, its status and the Connect/Disconnect button.</summary>
public class Sidebar : Panel
{
    private readonly Core _core;
    private readonly Dictionary<string, NavButton> _items = new();
    private readonly Panel _widgetList = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel, AutoScroll = true };
    private readonly List<WidgetNavItem> _widgetItems = new();
    private string _selectedKey = "dashboard";
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
        // Two groups: the core application, then the optional widgets. Top-docked items stack last-added-first, so the bottom group goes in first.
        var nav = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        nav.Controls.Add(_widgetList); // docked first so it takes the room that is left
        nav.Controls.Add(Caption("WIDGETS"));
        AddItems(nav,("dashboard", "", "Dashboard"), ("console", "", "Console"), ("players", "", "Players"));

        nav.Controls.Add(Caption("CORE"));

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

        DarkScroll.Apply(_widgetList);
        BuildWidgetList();
        HandleCreated += (_, _) => BeginInvoke(() => { if (_widgetItems.FirstOrDefault(w => w.Selected) is { } shown) _widgetList.ScrollControlIntoView(shown); });
        _core.Widgets.Changed += () => BeginInvoke(BuildWidgetList);

        _connect.Click += (_, _) => ConnectClicked?.Invoke();
        _core.StateChanged += RefreshState;
        _core.ServerChanged += RefreshState;
        RefreshState();
    }

    private static Label Caption(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Dim,
        Padding = new Padding(22, 12, 0, 0), BackColor = Theme.Panel,
    };

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
        if (key == "widgets") key = "widgets:" + WidgetCatalog.BuiltIn[0].Key;
        _selectedKey = key;
        foreach (var (k, b) in _items) b.Selected = k == key;
        foreach (var w in _widgetItems) w.Selected = w.Key == key;
        if (_widgetItems.FirstOrDefault(w => w.Selected) is { } shown && IsHandleCreated) BeginInvoke(() => _widgetList.ScrollControlIntoView(shown));
    }

    /// <summary>(Re)creates the list under the WIDGETS heading: the built-in widgets, then installed community ones, then a way to get more.</summary>
    private void BuildWidgetList()
    {
        foreach (var w in _widgetItems) { _widgetList.Controls.Remove(w); w.Dispose(); }
        _widgetItems.Clear();

        var entries = new List<WidgetNavItem>();
        foreach (var info in WidgetCatalog.AllWidgets(_core))
            entries.Add(new WidgetNavItem("widgets:" + info.Key, info.Name, info.Blurb, info.IsCommunity));
        entries.Add(new WidgetNavItem("widgets:" + WidgetsPage.ManageKey, "Get more widgets...", "Browse the library, or add one from a file or link.", false, isAction: true));

        foreach (var e in entries)
        {
            var key = e.Key;
            e.Click += (_, _) => Navigated?.Invoke(key);
            e.Selected = key == _selectedKey;
        }
        _widgetItems.AddRange(entries);
        // Top-docked controls stack last-added-first, so add in reverse.
        foreach (var e in Enumerable.Reverse(entries)) _widgetList.Controls.Add(e);
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

/// <summary>A compact row under the WIDGETS heading in the sidebar. Community widgets get a small green dot.</summary>
public class WidgetNavItem : Control
{
    private bool _hover;
    private bool _selected;
    private readonly bool _community;
    private readonly bool _action;

    public string Key { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public WidgetNavItem(string key, string name, string blurb, bool community, bool isAction = false)
    {
        Key = key;
        Text = name;
        _community = community;
        _action = isAction;
        Height = 32;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        Font = new Font("Segoe UI", 9.5f);
        Tips.Set(this, (community ? "Community widget\n" : isAction ? "" : "Built-in widget\n") + blurb);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);
        var r = new Rectangle(10, 2, Width - 20, Height - 4);
        if (_selected || _hover)
        {
            using var path = NavButton.Rounded(r, 7);
            using var br = new SolidBrush(_selected ? Theme.Input : Theme.Hover);
            g.FillPath(br, path);
        }
        if (_selected)
        {
            using var br = new SolidBrush(Theme.Accent);
            g.FillRectangle(br, 0, 8, 3, Height - 16);
        }
        if (_community)
        {
            using var dot = new SolidBrush(Theme.Ok);
            g.FillEllipse(dot, 36, Height / 2 - 3, 6, 6);
        }
        var fore = _selected ? Theme.Text : _action ? Theme.Accent : Theme.Muted;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(56, 0, Width - 62, Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
