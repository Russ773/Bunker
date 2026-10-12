using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>A page that is split into sections chosen from a list or a row of tabs.</summary>
public interface ISectionPage
{
    void ShowSection(string key);
}

/// <summary>A small rounded label saying where a feature comes from: Core, Built-in or Community.</summary>
public class OriginPill : Control
{
    public WidgetOrigin Origin { get; }

    public OriginPill(WidgetOrigin origin)
    {
        Origin = origin;
        Text = WidgetCatalog.Label(origin).ToUpperInvariant();
        Font = new Font("Segoe UI Semibold", 8f);
        Height = 22;
        Width = TextRenderer.MeasureText(Text, Font).Width + 18;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
    }

    public static Color ColourOf(WidgetOrigin origin) => origin switch
    {
        WidgetOrigin.Core => Theme.Accent,
        WidgetOrigin.BuiltIn => Theme.Chat,
        _ => Theme.Ok,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        var colour = ColourOf(Origin);
        var r = new Rectangle(0, 1, Width - 1, Height - 3);
        using (var path = NavButton.Rounded(r, 9))
        {
            using var fill = new SolidBrush(Color.FromArgb(Theme.IsLight ? 28 : 46, colour));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(110, colour));
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, r, colour, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>One row in the Widgets list: the widget's name and a one-line description.</summary>
public class WidgetRailItem : Control
{
    private bool _hover;
    private bool _selected;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public string Blurb { get; }

    public WidgetRailItem(string name, string blurb)
    {
        Text = name;
        Blurb = blurb;
        Height = 58;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        Tips.Set(this, name + "\n" + blurb);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);
        var r = new Rectangle(8, 2, Width - 16, Height - 4);
        if (_selected || _hover)
        {
            using var path = NavButton.Rounded(r, 8);
            using var br = new SolidBrush(_selected ? Theme.Input : Theme.Hover);
            g.FillPath(br, path);
        }
        if (_selected)
        {
            using var br = new SolidBrush(Theme.Accent);
            g.FillRectangle(br, 0, 12, 3, Height - 24);
        }
        var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Text, Theme.Semibold, new Rectangle(20, 9, Width - 36, 22), _selected ? Theme.Text : Theme.Text, flags);
        TextRenderer.DrawText(g, Blurb, Theme.Small, new Rectangle(20, 31, Width - 36, 18), Theme.Muted, flags);
    }
}

/// <summary>
/// The Widgets page: a list of every widget on the left (the ones that come with Bunker, then the ones you installed) and the
/// chosen widget on the right. The core application (Dashboard, Console, Players) is not listed here.
/// </summary>
public class WidgetsPage : Panel, ISectionPage
{
    private const string ManageKey = "manage";

    private readonly Core _core;
    private readonly Panel _list = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel, AutoScroll = true };
    private readonly Panel _info = new() { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Bg };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    private readonly Dictionary<string, (WidgetRailItem Item, Panel Host, WidgetInfo Info)> _sections = new();
    private readonly HashSet<string> _built = new();
    private readonly Button _manage = Theme.GhostButton("Get more widgets...");
    private string _current = WidgetCatalog.BuiltIn[0].Key;

    public WidgetsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        // Left: the list of widgets with a button underneath for installing more.
        var rail = new Panel { Dock = DockStyle.Left, Width = 268, BackColor = Theme.Panel };
        rail.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, rail.Width - 1, rail.Height - 1); };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Panel, Padding = new Padding(14, 12, 14, 12) };
        footer.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        _manage.Dock = DockStyle.Fill;
        _manage.Click += (_, _) => ShowSection(ManageKey);
        footer.Controls.Add(_manage);
        rail.Controls.Add(_list);
        rail.Controls.Add(footer);
        DarkScroll.Apply(_list);

        // Right: a strip saying what the widget is, then the widget itself.
        var gap = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Theme.Bg };
        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _content.Controls.Add(_manageHost);
        main.Controls.Add(_content);
        main.Controls.Add(_info);

        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(main);
        body.Controls.Add(gap);
        body.Controls.Add(rail);

        var page = new Page("Widgets", "Optional add-ons. Dashboard, Console and Players are part of Bunker itself, so they are not listed here", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        _core.Widgets.Changed += () => BeginInvoke(RebuildList);
        RebuildList();

        // When the page appears, make sure the open widget is visible in the list.
        VisibleChanged += (_, _) =>
        {
            if (Visible && _sections.TryGetValue(_current, out var s)) BeginInvoke(() => _list.ScrollControlIntoView(s.Item));
        };
    }

    // ---------------------------------------------------------------- the list

    /// <summary>(Re)creates the list, for example after a community widget was installed or removed.</summary>
    private void RebuildList()
    {
        foreach (var s in _sections.Values) { _list.Controls.Remove(s.Item); s.Item.Dispose(); _content.Controls.Remove(s.Host); s.Host.Dispose(); }
        foreach (var c in _list.Controls.OfType<Label>().ToList()) { _list.Controls.Remove(c); c.Dispose(); }
        _sections.Clear();
        _built.Clear();

        var built = WidgetCatalog.BuiltIn;
        var community = _core.Widgets.All.Select(WidgetCatalog.Community).ToList();

        // Top-docked controls stack with the last one added on top, so everything is added in reverse.
        var rows = new List<Control> { Header("BUILT-IN  ·  come with Bunker") };
        rows.AddRange(built.Select(Add));
        rows.Add(Header("COMMUNITY  ·  installed by you"));
        if (community.Count == 0)
            rows.Add(new Label
            {
                Text = "None installed yet. Use \"Get more widgets\" below to browse the library or add one from a file.",
                Dock = DockStyle.Top, Height = 64, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(16, 6, 14, 0),
            });
        else rows.AddRange(community.Select(Add));

        foreach (var row in Enumerable.Reverse(rows)) _list.Controls.Add(row);

        ShowSection(_sections.ContainsKey(_current) || _current == ManageKey ? _current : built[0].Key);
    }

    private static Label Header(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Muted,
        Padding = new Padding(16, 14, 0, 0), BackColor = Theme.Panel,
    };

    private Control Add(WidgetInfo info)
    {
        var item = new WidgetRailItem(info.Name, info.Blurb);
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        _content.Controls.Add(host);
        item.Click += (_, _) => ShowSection(info.Key);
        _sections[info.Key] = (item, host, info);
        return item;
    }

    public void ShowSection(string key)
    {
        if (key != ManageKey && !_sections.ContainsKey(key)) key = WidgetCatalog.BuiltIn[0].Key;
        _current = key;
        foreach (var (k, s) in _sections) { s.Host.Visible = k == key; s.Item.Selected = k == key; }
        if (_sections.TryGetValue(key, out var chosen)) _list.ScrollControlIntoView(chosen.Item);
        _manage.Font = new Font(Theme.UI, key == ManageKey ? FontStyle.Bold : FontStyle.Regular);

        if (key == ManageKey)
        {
            foreach (var s in _sections.Values) { s.Host.Visible = false; s.Item.Selected = false; }
            _manageHost.Visible = true;
            if (_manageHost.Controls.Count == 0)
            {
                var wide = Card.Titled("Manage community widgets", new WidgetManagerView(_core));
                wide.Dock = DockStyle.Fill;
                _manageHost.Controls.Add(wide);
            }
            ShowInfo(null);
            return;
        }

        _manageHost.Visible = false;
        var (_, host, info) = _sections[key];
        host.Visible = true;
        if (_built.Add(key)) Build(key, host, info); // widgets are created the first time they are opened
        ShowInfo(info);
    }

    private readonly Panel _manageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };

    // ---------------------------------------------------------------- the strip above a widget

    /// <summary>Shows what kind of widget this is, a line about it and the things you can do with it.</summary>
    private void ShowInfo(WidgetInfo? info)
    {
        foreach (var c in _info.Controls.OfType<Control>().ToList()) { _info.Controls.Remove(c); c.Dispose(); }

        // The text is added first so it takes whatever room the pill and the buttons leave.
        var text = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 10, 12, 0) };
        _info.Controls.Add(text);

        if (info is null)
        {
            text.Padding = new Padding(2, 12, 0, 0);
            text.Text = "Whatever you install here appears in the list on the left and in the Layout picker.";
            return;
        }

        var by = "";
        if (info.IsCommunity && _core.Widgets.Get(info.CustomId) is { } def)
            by = $"By {(string.IsNullOrWhiteSpace(def.Author) ? "an unknown author" : def.Author)}" + (string.IsNullOrWhiteSpace(def.Version) ? "" : $"  ·  v{def.Version}") + "  ·  ";
        text.Text = by + info.Blurb;
        Tips.Set(text, text.Text);

        var pill = new OriginPill(info.Origin) { Location = new Point(2, 14) };
        var pillHost = new Panel { Dock = DockStyle.Left, Width = pill.Width + 14, BackColor = Theme.Bg };
        pillHost.Controls.Add(pill);
        _info.Controls.Add(pillHost);

        var buttons = new List<Button>();
        var layout = Theme.GhostButton("Add to Layout   ▾");
        layout.Width = 150;
        layout.Click += (_, _) => ShowLayoutMenu(layout, info);
        buttons.Add(layout);
        if (info.IsCommunity && _core.Widgets.Get(info.CustomId) is { } d)
        {
            var details = Theme.GhostButton("Details");
            details.Click += (_, _) => { using var f = new WidgetReviewForm(d, install: false); f.ShowDialog(FindForm()); };
            var remove = Theme.GhostButton("Remove");
            remove.Click += (_, _) =>
            {
                var ok = MessageBox.Show(FindForm(), $"Remove the widget \"{d.Name}\"?\n\nIf it is used in your Layout, that section will show a notice.",
                    "Remove widget", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok == DialogResult.Yes) _core.Widgets.Remove(d.Id);
            };
            buttons.AddRange(new[] { details, remove });
        }

        var actions = new Panel { Dock = DockStyle.Right, BackColor = Theme.Bg };
        var x = 0;
        foreach (var b in buttons)
        {
            b.Height = 32;
            b.Location = new Point(x, 10);
            actions.Controls.Add(b);
            x += b.Width + 8;
        }
        actions.Width = x;
        _info.Controls.Add(actions);
    }

    /// <summary>Offers each section of the current Layout, so a widget can be put where you want it with one click.</summary>
    private void ShowLayoutMenu(Button anchor, WidgetInfo info)
    {
        var layout = _core.Settings.Layout;
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem("Put this widget in section...") { Enabled = false, BackColor = Theme.Input, ForeColor = Theme.Muted });
        for (var i = 0; i < layout.SlotCount; i++)
        {
            var index = i;
            var current = layout.SlotAt(i) == WidgetKind.Custom
                ? _core.Widgets.Get(layout.CustomAt(i))?.Name ?? "missing widget"
                : Widgets.Name(layout.SlotAt(i));
            var item = new ToolStripMenuItem($"Section {i + 1}   (now: {current})") { BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) =>
            {
                layout.Slots[index] = info.Kind;
                if (layout.CustomIds is not { Length: >= 4 }) layout.CustomIds = new string?[4];
                layout.CustomIds[index] = info.CustomId;
                _core.Settings.Save();
                anchor.Text = $"Added to section {index + 1}  ✓";
                var timer = new System.Windows.Forms.Timer { Interval = 2500 };
                timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); if (!anchor.IsDisposed) anchor.Text = "Add to Layout   ▾"; };
                timer.Start();
            };
            menu.Items.Add(item);
        }
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // ---------------------------------------------------------------- building a widget

    private void Build(string key, Panel host, WidgetInfo info)
    {
        if (FullWidth.Contains(info.Kind))
        {
            // Wide widgets (tables, charts) use the whole page.
            var (wide, wideActions) = Widgets.Create(info.Kind, _core, 2, info.CustomId);
            var wideCard = Card.Titled(info.Name, wide, wideActions);
            wideCard.Dock = DockStyle.Fill;
            host.Controls.Add(wideCard);
            return;
        }

        // Form-style widgets sit on the left at a comfortable width; the live console sits beside them so the
        // server's reply to every button is visible straight away.
        var (tool, actions) = Widgets.Create(info.Kind, _core, 2, info.CustomId);
        var card = Card.Titled(info.Name, tool, actions);
        card.Dock = DockStyle.Left;
        card.Width = 600;
        // Leave room for the console on smaller windows.
        host.Resize += (_, _) => card.Width = Math.Clamp((int)(host.Width * 0.55), 380, 600);

        var console = new ConsoleView(_core);
        var consoleCard = Card.Titled("Server replies", console, console.HeaderActions);
        consoleCard.Dock = DockStyle.Fill;

        var gap = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Theme.Bg };
        host.Controls.Add(consoleCard); // docked last: takes the space that is left
        host.Controls.Add(gap);
        host.Controls.Add(card);
    }

    /// <summary>Widgets that need the full width instead of a form column.</summary>
    private static readonly HashSet<WidgetKind> FullWidth = new() { WidgetKind.Health, WidgetKind.Scheduler, WidgetKind.PlayerDb, WidgetKind.Activity, WidgetKind.ChatMonitor };
}
