using System.Drawing;

namespace Bunker;

/// <summary>A page that is split into sections chosen from a row of tabs.</summary>
public interface ISectionPage
{
    void ShowSection(string key);
}

/// <summary>Admin tools on their own page: one tab per tool, each showing that tool at a comfortable width.</summary>
public class ToolsPage : Panel, ISectionPage
{
    private sealed record Tab(string Key, string Glyph, string Title, WidgetKind Kind);

    private static readonly Tab[] Tabs =
    {
        new("announce", "", "Announce", WidgetKind.Announce),
        new("actions", "", "Quick actions", WidgetKind.QuickActions),
        new("moderation", "", "Moderation", WidgetKind.Moderation),
        new("teleport", "", "Teleport", WidgetKind.Teleport),
        new("shortcuts", "", "Shortcuts", WidgetKind.Shortcuts),
        new("give", "", "Give items", WidgetKind.GiveItems),
    };

    private readonly Core _core;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
    private readonly Dictionary<string, (TabButton Button, Panel Host, WidgetKind Kind)> _sections = new();
    private readonly HashSet<string> _built = new();

    public ToolsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        var strip = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel };
        strip.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, strip.Width - 1, strip.Height - 1); };

        foreach (var t in Tabs)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            _content.Controls.Add(host);
            var key = t.Key;
            var button = new TabButton(t.Glyph, t.Title) { Dock = DockStyle.Left };
            button.Click += (_, _) => ShowSection(key);
            _sections[key] = (button, host, t.Kind);
        }
        foreach (var t in Tabs.Reverse()) strip.Controls.Add(_sections[t.Key].Button);

        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(_content);
        body.Controls.Add(strip);

        var page = new Page("Tools", "Admin tools for the server you are connected to", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        ShowSection(Tabs[0].Key);
    }

    public void ShowSection(string key)
    {
        if (!_sections.ContainsKey(key)) key = Tabs[0].Key;
        foreach (var (k, s) in _sections) { s.Host.Visible = k == key; s.Button.Selected = k == key; }
        if (_built.Add(key)) Build(key); // tools are created the first time their tab is opened
    }

    private void Build(string key)
    {
        var (_, host, kind) = _sections[key];
        var (tool, actions) = Widgets.Create(kind, _core);

        // Tools are laid out for a narrow column, so keep them at a readable width instead of stretching across the page.
        var inner = new Panel { BackColor = Theme.Panel };
        tool.Dock = DockStyle.Left;
        tool.Width = 600;
        inner.Controls.Add(tool);

        var card = Card.Titled(Widgets.Name(kind), inner, actions);
        card.Dock = DockStyle.Left;
        card.Width = 680;
        host.Controls.Add(card);
    }
}
