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
        var conn = new Panel { Dock = DockStyle.Bottom, Height = 142, Padding = new Padding(20, 14, 20, 16), BackColor = Theme.Panel };
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

        var lower = new Panel { Dock = DockStyle.Bottom, Height = 128, BackColor = Theme.Panel, Padding = new Padding(0, 8, 0, 6) };
        AddItems(lower, ("servers", "", "Multi Server"), ("layout", "", "Layout"), ("settings", "", "Settings"));
        lower.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 14, 0, lower.Width - 14, 0); };

        // Brand (top)
        var brand = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Panel };
        if (Theme.AppIcon is { } icon)
            brand.Controls.Add(new PictureBox { Image = SplashForm.LoadLogo() ?? icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(38, 38), Location = new Point(20, 16), BackColor = Theme.Panel });
        brand.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Text, AutoSize = true, Location = new Point(66, 14) });
        brand.Controls.Add(new Label { Text = Updater.VersionText, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(68, 38) });

        Controls.Add(nav);
        Controls.Add(brand);
        Controls.Add(lower);
        Controls.Add(conn);

        DarkScroll.Apply(_widgetList);
        BuildWidgetList();
        HandleCreated += (_, _) => BeginInvoke(() => { if (_widgetItems.FirstOrDefault(w => w.Selected) is { } shown) _widgetList.ScrollControlIntoView(shown); });
        _core.Widgets.Changed += () => BeginInvoke(BuildWidgetList);
        _core.WidgetUpdates.Changed += () => { if (IsHandleCreated) BeginInvoke(BuildWidgetList); };
        _core.WidgetPrefsChanged += () => { if (IsHandleCreated) BeginInvoke(BuildWidgetList); };

        _connect.Click += (_, _) => ConnectClicked?.Invoke();
        _core.StateChanged += RefreshState;
        _core.ServerChanged += RefreshState;
        RefreshState();
    }

    private static Label Caption(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Dim,
        Padding = new Padding(22, 8, 0, 0), BackColor = Theme.Panel,
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
        if (key == "widgets") key = WidgetsKey(WidgetsPage.HubKey);
        var before = _selectedKey;
        _selectedKey = key;
        foreach (var (k, b) in _items) b.Selected = k == key;
        // The row for the open widget (when it is not a favourite) comes and goes as you move between widgets.
        if (before != key && (before.StartsWith("widgets:") || key.StartsWith("widgets:"))) BuildWidgetList();
        foreach (var w in _widgetItems) w.Selected = w.Key == key;
        if (_widgetItems.FirstOrDefault(w => w.Selected) is { } shown && IsHandleCreated) BeginInvoke(() => _widgetList.ScrollControlIntoView(shown));
    }

    private static string WidgetsKey(string widget) => "widgets:" + widget;

    /// <summary>
    /// (Re)creates the list under the WIDGETS heading: one "All widgets" row that opens the full page of tiles, then only your favourites
    /// (and the widget that is open, if it is not one). Everything else is on the Widgets page, so this list never needs to scroll much.
    /// </summary>
    private void BuildWidgetList()
    {
        foreach (var w in _widgetItems) { _widgetList.Controls.Remove(w); w.Dispose(); }
        _widgetItems.Clear();

        var settings = _core.Settings;
        var all = WidgetCatalog.AllWidgets(_core).ToList();
        var openKey = _selectedKey.StartsWith("widgets:") ? _selectedKey["widgets:".Length..] : null;
        if (openKey == WidgetsPage.HubKey || openKey == WidgetsPage.ManageKey) openKey = null;
        var rows = WidgetGroups.Sidebar(all, settings.FavouriteWidgets, settings.HiddenWidgets, openKey);

        var anyUpdate = all.Any(i => i.IsCommunity && _core.WidgetUpdates.HasUpdate(i.CustomId));
        var hub = new WidgetNavItem(WidgetsKey(WidgetsPage.HubKey), $"All widgets  ({all.Count})",
            "Every widget as a tile, with search. Star the ones you use most and they are listed here." + (anyUpdate ? "\n\nA newer version of a community widget is available." : ""),
            false, isAction: true, hasUpdate: anyUpdate);
        hub.Click += (_, _) => Navigated?.Invoke(WidgetsKey(WidgetsPage.HubKey));

        var entries = new List<WidgetNavItem> { hub };
        foreach (var info in rows) entries.Add(MakeWidgetItem(info));

        foreach (var e in entries) e.Selected = e.Key == _selectedKey || (e == hub && _selectedKey == WidgetsKey(WidgetsPage.ManageKey));
        _widgetItems.AddRange(entries);
        // Top-docked controls stack last-added-first, so add in reverse.
        foreach (var e in Enumerable.Reverse(entries)) _widgetList.Controls.Add(e);
    }

    private WidgetNavItem MakeWidgetItem(WidgetInfo info)
    {
        var key = WidgetsKey(info.Key);
        var update = info.IsCommunity && _core.WidgetUpdates.HasUpdate(info.CustomId);
        var favourite = _core.Settings.FavouriteWidgets.Contains(info.Key);
        var item = new WidgetNavItem(key, info.Name, (update ? "A newer version of this widget is available.\n\n" : "") + info.Blurb + "\n\nClick the star to " + (favourite ? "remove it from" : "add it to") + " your favourites.", info.IsCommunity, hasUpdate: update, canStar: true, starred: favourite);
        item.Click += (_, _) => Navigated?.Invoke(key);
        item.StarClicked += () => _core.ToggleFavouriteWidget(info.Key);
        return item;
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
    private readonly bool _dim;
    private readonly bool _hasUpdate;
    private readonly bool _canStar;
    private readonly bool _starred;
    private bool _pressedOnStar;

    /// <summary>The star at the right of the row was clicked (instead of the row itself).</summary>
    public event Action? StarClicked;

    private bool OnStar(Point p) => _canStar && p.X >= Width - 40;

    public string Key { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public WidgetNavItem(string key, string name, string blurb, bool community, bool isAction = false, bool dim = false, bool hasUpdate = false, bool canStar = false, bool starred = false)
    {
        _dim = dim;
        _canStar = canStar;
        _starred = starred;
        _hasUpdate = hasUpdate;
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
    protected override void OnMouseDown(MouseEventArgs e) { _pressedOnStar = e.Button == MouseButtons.Left && OnStar(e.Location); base.OnMouseDown(e); }

    protected override void OnClick(EventArgs e)
    {
        // A click on the star stars or un-stars the widget; it does not open it.
        if (_pressedOnStar) { _pressedOnStar = false; StarClicked?.Invoke(); return; }
        base.OnClick(e);
    }

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
        if (_hasUpdate)
        {
            using var arrow = new SolidBrush(Theme.Accent);
            g.FillEllipse(arrow, Width - 56, Height / 2 - 4, 8, 8); // an orange dot: a newer version is available
        }
        if (_canStar && (_starred || _hover || _selected))
        {
            // A filled orange star for a favourite; a faint outline on hover so it can be found.
            TextRenderer.DrawText(g, _starred ? "★" : "☆", new Font("Segoe UI Symbol", 11f), new Rectangle(Width - 38, 0, 28, Height),
                _starred ? Theme.Accent : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        var fore = _selected ? Theme.Text : _action ? Theme.Accent : _dim ? Theme.Dim : Theme.Muted;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(56, 0, Width - (_canStar ? 100 : 62), Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
