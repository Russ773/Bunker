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

/// <summary>
/// The Widgets page: shows the widget picked from the WIDGETS list in the sidebar, with a strip saying where it comes from.
/// The core application (Dashboard, Console, Players) is not a widget and has its own entries above that list.
/// </summary>
public class WidgetsPage : Panel, ISectionPage
{
    public const string ManageKey = "manage";

    private readonly Core _core;
    private readonly Panel _info = new() { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Bg };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    private readonly Dictionary<string, Panel> _hosts = new();
    private readonly Panel _manageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
    private string _current = WidgetCatalog.BuiltIn[0].Key;

    public WidgetsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        _content.Controls.Add(_manageHost);
        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(_content);
        body.Controls.Add(_info);

        var page = new Page("Widgets", "Optional add-ons. Pick one from the WIDGETS list in the menu on the left", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        // A widget was installed or removed: drop everything that was built and show the current one again (or the first).
        _core.Widgets.Changed += () => BeginInvoke(Reset);
        _core.WidgetUpdates.Changed += () => { if (IsHandleCreated) BeginInvoke(() => { if (_current != ManageKey) ShowSection(_current); }); };
        ShowSection(_current);
    }

    private void Reset()
    {
        foreach (var host in _hosts.Values) { _content.Controls.Remove(host); host.Dispose(); }
        _hosts.Clear();
        ShowSection(_current);
    }

    private WidgetInfo? Find(string key) =>
        WidgetCatalog.AllWidgets(_core).FirstOrDefault(i => i.Key == key);

    public void ShowSection(string key)
    {
        foreach (var h in _hosts.Values) h.Visible = false;
        _manageHost.Visible = false;

        if (key == ManageKey)
        {
            _current = key;
            _manageHost.Visible = true;
            if (_manageHost.Controls.Count == 0)
            {
                var manager = Card.Titled("Get more widgets", new WidgetManagerView(_core));
                manager.Dock = DockStyle.Fill;
                _manageHost.Controls.Add(manager);
            }
            ShowInfo(null);
            return;
        }

        var info = Find(key) ?? WidgetCatalog.BuiltIn[0];
        _current = info.Key;
        if (!_hosts.TryGetValue(info.Key, out var host))
        {
            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            _content.Controls.Add(host);
            _hosts[info.Key] = host;
            Build(info.Key, host, info); // widgets are created the first time they are opened
        }
        host.Visible = true;
        ShowInfo(info);
    }

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
            text.Text = "Install, review and remove community widgets. Whatever you install appears in the WIDGETS list and in the Layout picker.";
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
        if (info.IsCommunity && _core.WidgetUpdates.Find(info.CustomId) is { } update)
        {
            var upgrade = Theme.PrimaryButton("Update Available");
            upgrade.Width = 150;
            upgrade.Click += (_, _) => _core.WidgetUpdates.ReviewAndInstall(update, FindForm());
            buttons.Insert(0, upgrade);
        }
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
        // A community widget that shows its own replies (an "output" box) does not need the console beside it, and its tables are wide.
        var showsReplies = info.Kind == WidgetKind.Custom && _core.Widgets.Get(info.CustomId) is { } def && def.Controls.Any(c => c.Type == "output");
        if (FullWidth.Contains(info.Kind) || showsReplies)
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
