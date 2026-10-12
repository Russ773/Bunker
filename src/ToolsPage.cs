using System.Drawing;

namespace Bunker;

/// <summary>A page that is split into sections chosen from a row of tabs.</summary>
public interface ISectionPage
{
    void ShowSection(string key);
}

/// <summary>Admin tools on their own page: one tab per tool, then one per installed community widget, then Widgets (manage them).</summary>
public class ToolsPage : Panel, ISectionPage
{
    private sealed record Tab(string Key, string Glyph, string Title, WidgetKind Kind, string? CustomId = null);

    private static readonly Tab[] BuiltIn =
    {
        new("announce", "", "Announce", WidgetKind.Announce),
        new("actions", "", "Quick actions", WidgetKind.QuickActions),
        new("moderation", "", "Moderation", WidgetKind.Moderation),
        new("teleport", "", "Teleport", WidgetKind.Teleport),
        new("shortcuts", "", "Shortcuts", WidgetKind.Shortcuts),
        new("give", "", "Give items", WidgetKind.GiveItems),
    };

    private const string ManageKey = "widgets";

    private readonly Core _core;
    private readonly Panel _strip = new() { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
    private readonly Dictionary<string, (TabButton Button, Panel Host, Tab Tab)> _sections = new();
    private readonly HashSet<string> _built = new();
    private string _current = BuiltIn[0].Key;
    private readonly List<string> _order = new();
    private readonly Button _more = Theme.GhostButton("");

    public ToolsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        _strip.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, _strip.Width - 1, _strip.Height - 1); };

        // When the tabs do not all fit, the extra ones move into this button's menu.
        _more.Font = new Font("Segoe MDL2 Assets", 12f);
        _more.Dock = DockStyle.Right;
        _more.Width = 48;
        _more.Visible = false;
        _more.Click += (_, _) => ShowMoreMenu();
        _strip.Controls.Add(_more);
        _strip.Resize += (_, _) => FitTabs();

        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(_content);
        body.Controls.Add(_strip);

        var page = new Page("Tools", "Admin tools for the server you are connected to", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        _core.Widgets.Changed += () => BeginInvoke(RebuildTabs);
        RebuildTabs();
    }

    private IEnumerable<Tab> AllTabs()
    {
        foreach (var t in BuiltIn) yield return t;
        foreach (var d in _core.Widgets.All) yield return new Tab("w:" + d.Id, "", d.Name, WidgetKind.Custom, d.Id);
        yield return new Tab(ManageKey, "", "Widgets", WidgetKind.Custom);
    }

    /// <summary>(Re)creates the tabs, for example after a community widget was installed or removed.</summary>
    private void RebuildTabs()
    {
        foreach (var s in _sections.Values) { _strip.Controls.Remove(s.Button); s.Button.Dispose(); _content.Controls.Remove(s.Host); s.Host.Dispose(); }
        _sections.Clear();
        _built.Clear();

        var tabs = AllTabs().ToList();
        foreach (var t in tabs)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            _content.Controls.Add(host);
            var key = t.Key;
            var button = new TabButton(t.Glyph, t.Title) { Dock = DockStyle.Left };
            button.Click += (_, _) => ShowSection(key);
            _sections[key] = (button, host, t);
        }
        foreach (var t in Enumerable.Reverse(tabs)) _strip.Controls.Add(_sections[t.Key].Button);
        _order.Clear();
        _order.AddRange(tabs.Select(t => t.Key));

        ShowSection(_sections.ContainsKey(_current) ? _current : tabs[0].Key);
        FitTabs();
    }

    /// <summary>Shows as many tabs as fit; the rest are reached through the "more" button.</summary>
    private void FitTabs()
    {
        if (_order.Count == 0 || _strip.Width < 100) return;
        var available = _strip.Width - 4;
        var all = _order.Sum(k => _sections[k].Button.Width);
        var overflow = all > available;
        if (overflow) available -= _more.Width;
        _more.Visible = overflow;

        var used = 0;
        foreach (var key in _order)
        {
            var b = _sections[key].Button;
            used += b.Width;
            b.Visible = !overflow || used <= available;
        }
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        foreach (var key in _order)
        {
            var (_, _, tab) = _sections[key];
            var k = key;
            var item = new ToolStripMenuItem(tab.Title) { Checked = k == _current, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) => ShowSection(k);
            menu.Items.Add(item);
        }
        menu.Show(_more, new Point(_more.Width - menu.PreferredSize.Width, _more.Height));
    }

    public void ShowSection(string key)
    {
        if (!_sections.ContainsKey(key)) key = BuiltIn[0].Key;
        _current = key;
        foreach (var (k, s) in _sections) { s.Host.Visible = k == key; s.Button.Selected = k == key; }
        if (_built.Add(key)) Build(key); // tools are created the first time their tab is opened
    }

    private void Build(string key)
    {
        var (_, host, tab) = _sections[key];

        Control tool;
        Control[] actions = Array.Empty<Control>();
        var width = 600;
        if (key == ManageKey)
        {
            tool = new WidgetManagerView(_core);
            width = 880;
        }
        else (tool, actions) = Widgets.Create(tab.Kind, _core, 2, tab.CustomId);

        // Tools are laid out for a narrow column, so keep them at a readable width instead of stretching across the page.
        var inner = new Panel { BackColor = Theme.Panel };
        tool.Dock = DockStyle.Left;
        tool.Width = width;
        inner.Controls.Add(tool);

        var card = Card.Titled(tab.Title, inner, actions);
        card.Dock = DockStyle.Left;
        card.Width = width + 80;
        host.Controls.Add(card);
    }
}
