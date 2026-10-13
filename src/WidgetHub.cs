using System.Drawing;

namespace Bunker;

/// <summary>One heading and the widgets under it on the Widgets page.</summary>
public sealed record WidgetGroup(string Title, List<WidgetInfo> Widgets, bool Dim = false);

/// <summary>Decides which widgets appear in which group, and which few appear in the sidebar. Kept free of controls so it can be tested.</summary>
public static class WidgetGroups
{
    public const string Favourites = "Favourites";
    public const string BuiltIn = "Built in";
    public const string Community = "Community";
    public const string Hidden = "Hidden";

    /// <summary>
    /// With no search: Favourites, Built in, Community and Hidden, leaving out any that are empty. A widget is in one group only
    /// (hidden beats favourite beats where it came from). With a search: one group of everything that matches, hidden ones included.
    /// </summary>
    public static List<WidgetGroup> Build(IEnumerable<WidgetInfo> widgets, ICollection<string> favourites, ICollection<string> hidden, string? query)
    {
        var all = widgets.ToList();
        var q = (query ?? "").Trim();
        if (q.Length > 0)
        {
            var hits = all.Where(w => w.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || w.Blurb.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            return new List<WidgetGroup> { new($"Results ({hits.Count})", hits) };
        }

        var shown = all.Where(w => !hidden.Contains(w.Key)).ToList();
        var groups = new List<WidgetGroup>
        {
            // Stars keep the order they were given in, so the group reads the way the sidebar does.
            new(Favourites, favourites.Select(k => shown.FirstOrDefault(w => w.Key == k)).OfType<WidgetInfo>().ToList()),
            new(BuiltIn, shown.Where(w => !favourites.Contains(w.Key) && w.Origin == WidgetOrigin.BuiltIn).ToList()),
            new(Community, shown.Where(w => !favourites.Contains(w.Key) && w.Origin == WidgetOrigin.Community).ToList()),
            new(Hidden, all.Where(w => hidden.Contains(w.Key)).ToList(), Dim: true),
        };
        return groups.Where(g => g.Widgets.Count > 0).ToList();
    }

    /// <summary>Hides or shows a widget. A hidden widget cannot also be a favourite, so hiding one un-stars it.</summary>
    public static void SetHidden(List<string> favourites, List<string> hidden, string key, bool hide)
    {
        if (hide) { if (!hidden.Contains(key)) hidden.Add(key); favourites.Remove(key); }
        else hidden.Remove(key);
    }

    /// <summary>
    /// What the sidebar lists under "All widgets": the favourites in the order they were starred, then the widget that is open if it is
    /// not one of them (so there is always a row saying where you are).
    /// </summary>
    public static List<WidgetInfo> Sidebar(IEnumerable<WidgetInfo> widgets, ICollection<string> favourites, ICollection<string> hidden, string? openKey)
    {
        var all = widgets.ToList();
        var rows = favourites.Where(k => !hidden.Contains(k)).Select(k => all.FirstOrDefault(w => w.Key == k)).OfType<WidgetInfo>().ToList();
        if (openKey is not null && rows.All(w => w.Key != openKey) && all.FirstOrDefault(w => w.Key == openKey) is { } open) rows.Add(open);
        return rows;
    }
}

/// <summary>A tile on the Widgets page: the widget's name, a short description, a star, and where it came from.</summary>
public sealed class WidgetCard : Control
{
    private readonly WidgetInfo _info;
    private readonly bool _starred;
    private readonly bool _update;
    private readonly bool _dim;
    private bool _hover;
    private bool _pressedOnStar;

    public WidgetInfo Info => _info;
    public event Action? StarClicked;

    public WidgetCard(WidgetInfo info, bool starred, bool update, bool dim)
    {
        _info = info;
        _starred = starred;
        _update = update;
        _dim = dim;
        Height = 100;
        Width = 290;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        Tips.Set(this, info.Blurb + "\n\nClick to open. Right-click to star or hide it.");
    }

    private bool OnStar(Point p) => p.X >= Width - 40 && p.Y <= 34;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressedOnStar = e.Button == MouseButtons.Left && OnStar(e.Location); base.OnMouseDown(e); }

    protected override void OnClick(EventArgs e)
    {
        if (_pressedOnStar) { _pressedOnStar = false; StarClicked?.Invoke(); return; } // the star stars it; it does not open it
        base.OnClick(e);
    }

    /// <summary>The text cut at a word so it fits on two lines, ending in an ellipsis when something had to go.</summary>
    internal static string TwoLines(string text, Font font, int width)
    {
        bool Fits(string s) => TextRenderer.MeasureText(s, font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height <= font.Height * 2 + 2;
        if (Fits(text)) return text;
        var words = text.Split(' ');
        for (var n = words.Length - 1; n > 0; n--)
        {
            var candidate = string.Join(" ", words.Take(n)).TrimEnd(',', ';', ':', '.') + "…";
            if (Fits(candidate)) return candidate;
        }
        return text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(_hover ? Theme.Input : Theme.Panel);
        using (var pen = new Pen(_hover ? Theme.Accent : Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var nameColour = _dim ? Theme.Dim : Theme.Text;
        TextRenderer.DrawText(g, _info.Name, Theme.Semibold, new Rectangle(14, 10, Width - 56, 22), nameColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, TwoLines(_info.Blurb, Theme.Small, Width - 28), Theme.Small, new Rectangle(14, 36, Width - 28, Theme.Small.Height * 2 + 2), _dim ? Theme.Dim : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        if (_starred || _hover)
            TextRenderer.DrawText(g, _starred ? "★" : "☆", new Font("Segoe UI Symbol", 12f), new Rectangle(Width - 38, 6, 28, 26),
                _starred ? Theme.Accent : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var foot = new Rectangle(14, Height - 24, Width - 28, 18);
        var origin = WidgetCatalog.Label(_info.Origin).ToUpperInvariant();
        var originFont = new Font("Segoe UI Semibold", 7.5f);
        TextRenderer.DrawText(g, origin, originFont, foot, _dim ? Theme.Dim : OriginPill.ColourOf(_info.Origin), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (_update)
        {
            var x = TextRenderer.MeasureText(origin, originFont).Width + 10;
            using var dot = new SolidBrush(Theme.Accent);
            g.FillEllipse(dot, foot.X + x, foot.Y + 5, 8, 8);
            TextRenderer.DrawText(g, "Update available", originFont, new Rectangle(foot.X + x + 12, foot.Y, 120, foot.Height), Theme.Accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>
/// The Widgets page when no single widget is open: every widget as a tile, grouped (Favourites, Built in, Community, Hidden), with a search box.
/// It replaces a long scrolling list in the sidebar, which only has room for your favourites.
/// </summary>
public sealed class WidgetHubView : Panel
{
    private const int MinCardWidth = 280;
    private const int Gap = 12;
    private readonly Core _core;
    private readonly ModernField _search = new();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(14, 12, 0, 0) };
    // The tiles are placed by LayoutCards below (a plain scrolling panel), which keeps headings on their own row and tiles in even columns.
    private readonly Panel _flow = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 6, 6, 12) };

    /// <summary>A tile was clicked: the widget's key.</summary>
    public event Action<string>? OpenRequested;
    public event Action? GetMoreRequested;

    public WidgetHubView(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        DarkScroll.Apply(_flow);

        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Bg };
        var more = Theme.GhostButton("Get more widgets...");
        more.Height = 34;
        more.Dock = DockStyle.Right;
        _search.Width = 340;
        _search.Location = new Point(0, 8);
        _search.Box.PlaceholderText = "Search widgets";
        _count.Location = new Point(_search.Right + 6, 8);
        top.Controls.Add(_search);
        top.Controls.Add(_count);
        top.Controls.Add(more);
        more.Click += (_, _) => GetMoreRequested?.Invoke();

        Controls.Add(_flow);
        Controls.Add(top);

        _search.Box.TextChanged += (_, _) => Rebuild();
        _flow.SizeChanged += (_, _) => LayoutCards();
        _core.WidgetPrefsChanged += OnChanged;
        _core.Widgets.Changed += OnChanged;
        _core.WidgetUpdates.Changed += OnChanged;
        Disposed += (_, _) => { _core.WidgetPrefsChanged -= OnChanged; _core.Widgets.Changed -= OnChanged; _core.WidgetUpdates.Changed -= OnChanged; };
        Rebuild();
    }

    private void OnChanged()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(Rebuild);
    }

    private int _order;

    private void Rebuild()
    {
        _order = 0;
        var settings = _core.Settings;
        var all = WidgetCatalog.AllWidgets(_core).ToList();
        var groups = WidgetGroups.Build(all, settings.FavouriteWidgets, settings.HiddenWidgets, _search.Text);

        _flow.SuspendLayout();
        foreach (Control c in _flow.Controls.Cast<Control>().ToList()) { _flow.Controls.Remove(c); c.Dispose(); }

        var searching = _search.Text.Trim().Length > 0;
        if (!searching && settings.FavouriteWidgets.Count(k => all.Any(w => w.Key == k) && !settings.HiddenWidgets.Contains(k)) == 0)
            AddNote("Star a widget to keep it in the menu on the left. Click the ☆ on any tile.");
        foreach (var group in groups)
        {
            AddHeading(group.Title + (searching ? "" : $"   ·   {group.Widgets.Count}"));
            foreach (var info in group.Widgets) AddCard(info, group.Dim);
        }
        if (groups.Count == 0 || (searching && groups[0].Widgets.Count == 0)) AddNote("No widgets match that search.");

        _count.Text = $"{all.Count} widgets";
        _flow.ResumeLayout();
        LayoutCards();
    }

    private void AddHeading(string text)
    {
        var heading = new Label { Tag = _order++, Text = text.ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Muted, AutoSize = false, Height = 26, Margin = new Padding(0), TextAlign = ContentAlignment.BottomLeft };
        _flow.Controls.Add(heading);
    }

    private void AddNote(string text)
    {
        var note = new Label { Tag = _order++, Text = text, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Height = 26, Margin = new Padding(0), TextAlign = ContentAlignment.MiddleLeft };
        _flow.Controls.Add(note);
    }

    private void AddCard(WidgetInfo info, bool dim)
    {
        var settings = _core.Settings;
        var update = info.IsCommunity && _core.WidgetUpdates.HasUpdate(info.CustomId);
        var card = new WidgetCard(info, settings.FavouriteWidgets.Contains(info.Key), update, dim);
        card.Click += (_, _) => OpenRequested?.Invoke(info.Key);
        card.StarClicked += () => _core.ToggleFavouriteWidget(info.Key);
        card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(card, info, e.Location); };
        card.Tag = _order++;
        _flow.Controls.Add(card);
    }

    private void ShowMenu(Control anchor, WidgetInfo info, Point at)
    {
        var settings = _core.Settings;
        var starred = settings.FavouriteWidgets.Contains(info.Key);
        var hidden = settings.HiddenWidgets.Contains(info.Key);
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        ToolStripMenuItem Item(string text, Action action) { var i = new ToolStripMenuItem(text) { BackColor = Theme.Input, ForeColor = Theme.Text }; i.Click += (_, _) => action(); return i; }
        menu.Items.Add(Item("Open", () => OpenRequested?.Invoke(info.Key)));
        if (!hidden) menu.Items.Add(Item(starred ? "Remove from favourites" : "Add to favourites", () => _core.ToggleFavouriteWidget(info.Key)));
        menu.Items.Add(Item(hidden ? "Show again" : "Hide from this page", () => _core.SetWidgetHidden(info.Key, !hidden)));
        menu.Items.Add(new ToolStripMenuItem("Hidden widgets stay available in the Layout picker") { Enabled = false, BackColor = Theme.Input, ForeColor = Theme.Muted });
        menu.Show(anchor, at);
    }

    /// <summary>Places everything: a heading or note takes a whole row, tiles fill rows in even columns, and the panel scrolls to fit.</summary>
    private void LayoutCards()
    {
        var available = _flow.ClientSize.Width - _flow.Padding.Horizontal;
        if (available <= 0) return;
        var columns = Math.Max(1, (available + Gap) / (MinCardWidth + Gap));
        var width = (available - (columns - 1) * Gap) / columns;

        _flow.SuspendLayout();
        _flow.AutoScrollPosition = Point.Empty; // positions below are from the top; the scroll offset would shift them
        var y = _flow.Padding.Top;
        var column = 0;
        foreach (Control c in _flow.Controls.Cast<Control>().OrderBy(c => (int)(c.Tag ?? 0)))
        {
            if (c is WidgetCard)
            {
                c.Width = width;
                c.Location = new Point(column * (width + Gap), y);
                if (++column == columns) { column = 0; y += c.Height + Gap; }
                continue;
            }
            if (column > 0) { column = 0; y += WidgetCardHeight + Gap; } // finish a row that was not full
            c.Width = available;
            c.Location = new Point(0, y + 6);
            y += c.Height + 10;
        }
        if (column > 0) y += WidgetCardHeight + Gap;
        _flow.AutoScrollMinSize = new Size(0, y + _flow.Padding.Bottom);
        _flow.ResumeLayout(true);
    }

    private const int WidgetCardHeight = 100;
}
