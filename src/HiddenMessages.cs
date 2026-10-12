using System.Drawing;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>One rule for hiding console lines: everything from a plugin, or any line containing some words.</summary>
public class HideRule
{
    public const string PluginKind = "plugin";
    public const string WordsKind = "words";

    public string Kind { get; set; } = WordsKind;
    public string Value { get; set; } = "";

    public bool IsPlugin => Kind == PluginKind;
    public override string ToString() => IsPlugin ? $"Plugin:  {Value}" : $"Words:  {Value}";
}

/// <summary>Rules for console lines you never want to see (for example a plugin that logs every few seconds).</summary>
public static class ConsoleFilter
{
    public const int MaxLength = 80;
    private static readonly Regex TimePrefix = new(@"^\d{2}:\d{2}:\d{2}\s+", RegexOptions.Compiled);
    // Bracketed words that mark a kind of message rather than a plugin, so they are not offered as plugins.
    private static readonly HashSet<string> LevelTags = new(StringComparer.OrdinalIgnoreCase) { "Error", "Warning", "Info", "Debug", "Log", "Chat" };
    private static readonly Regex LeadingTag = new(@"^\[(?<name>[^\]]{1,40})\]", RegexOptions.Compiled);

    /// <summary>
    /// True if a rule hides the line. A plugin rule matches lines that start with that plugin's name in square brackets, such as
    /// "[Economics] Saving balances"; a words rule matches any line containing the text. Capitals do not matter. Your own commands
    /// and Bunker's notices are never hidden.
    /// </summary>
    public static bool IsHidden(IReadOnlyList<HideRule> rules, LogEntry entry)
    {
        if (rules.Count == 0 || entry.Kind is LogKind.Command or LogKind.Info) return false;
        var plugin = PluginOf(entry.Text);
        foreach (var rule in rules)
        {
            if (rule.Value.Length == 0) continue;
            if (rule.IsPlugin)
            {
                if (plugin is not null && plugin.Equals(rule.Value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (entry.Text.Contains(rule.Value, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The plugin named at the start of a line, such as "Economics" for "[Economics] Saving balances", or null.</summary>
    public static string? PluginOf(string text) =>
        LeadingTag.Match(WithoutTime(text)) is { Success: true } m ? m.Groups["name"].Value.Trim() : null;

    /// <summary>A line as shown in the console starts with its time; the text after it is what the rules are written against.</summary>
    public static string WithoutTime(string line) => TimePrefix.Replace(line, "").Trim();

    /// <summary>The plugins that have written to the console, busiest first, so they can be picked instead of typed.</summary>
    public static List<(string Name, int Lines)> PluginsSeen(IEnumerable<LogEntry> entries) =>
        entries.Where(e => e.Kind is LogKind.Normal or LogKind.Warning or LogKind.Error)
            .Select(e => PluginOf(e.Text)).Where(n => !string.IsNullOrWhiteSpace(n) && !LevelTags.Contains(n!))
            .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First()!, Lines: g.Count()))
            .OrderByDescending(x => x.Lines).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Tidies what was typed: no control characters, no brackets around a plugin name, a sensible length.</summary>
    public static string Clean(string kind, string value)
    {
        var v = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (kind == HideRule.PluginKind) v = v.Trim('[', ']', ' ');
        return v.Length > MaxLength ? v[..MaxLength] : v;
    }

    /// <summary>Adds a rule if it is new. Returns true if the list changed.</summary>
    public static bool Add(AppSettings settings, string kind, string value)
    {
        var v = Clean(kind, value);
        if (v.Length == 0) return false;
        if (settings.ConsoleHidden.Any(r => r.Kind == kind && r.Value.Equals(v, StringComparison.OrdinalIgnoreCase))) return false;
        settings.ConsoleHidden.Add(new HideRule { Kind = kind, Value = v });
        settings.Save();
        return true;
    }
}

/// <summary>Choose what to hide from the console: whole plugins, or lines containing certain words. Add, review and remove rules here.</summary>
public class HiddenMessagesForm : Form
{
    private readonly Core _core;
    private readonly ModernField _value = new();
    private readonly ListBox _seen = NewList(34);
    private readonly ListBox _rules = NewList(30);
    private readonly Button _remove = Theme.GhostButton("Remove selected");
    private readonly Button _pluginMode = Theme.PrimaryButton("A plugin");
    private readonly Button _wordsMode = Theme.GhostButton("Words in the message");
    private readonly Label _hint = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = false };
    private string _mode = HideRule.PluginKind;

    private static ListBox NewList(int itemHeight) => new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, IntegralHeight = false,
        ItemHeight = itemHeight, DrawMode = DrawMode.OwnerDrawFixed,
    };

    public HiddenMessagesForm(Core core)
    {
        _core = core;
        Theme.Apply(this);
        Text = "Hide console messages";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 640);

        // 1. What to hide
        var intro = new Label
        {
            Text = "Hidden lines are still received. Show them again by removing the rule below, or use Filter → Show hidden messages for now.",
            Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(2, 2, 2, 0),
        };

        _pluginMode.Height = _wordsMode.Height = 36;
        _pluginMode.Margin = new Padding(0, 0, 8, 0);
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Bg };
        modes.Controls.Add(new Label { Text = "Hide", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 9, 12, 0) });
        modes.Controls.Add(_pluginMode);
        modes.Controls.Add(_wordsMode);

        var addButton = Theme.PrimaryButton("Hide");
        addButton.Height = 38;
        addButton.Width = 90;
        addButton.Dock = DockStyle.Right;
        _value.Box.MaxLength = ConsoleFilter.MaxLength;
        _value.Dock = DockStyle.Fill;
        var gap = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Theme.Bg };
        var addRow = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 6, 0, 6), BackColor = Theme.Bg };
        addRow.Controls.Add(_value);
        addRow.Controls.Add(gap);
        addRow.Controls.Add(addButton);

        var hintRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Bg };
        hintRow.Controls.Add(_hint);

        // 2. Plugins seen in the console now
        var seenCaption = new Label { Text = "Plugins that have written to the console (click one to fill it in)", Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 6, 0, 0) };
        var seenHost = Framed(_seen);
        seenHost.Dock = DockStyle.Top;
        seenHost.Height = 150;

        // 3. Rules in force
        var rulesCaption = new Label { Text = "Hidden now", Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 12, 0, 0) };
        var rulesHost = Framed(_rules);

        _remove.Height = 36;
        var close = Theme.GhostButton("Close");
        close.Height = 36;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Bg };
        _remove.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(_remove);
        bar.Controls.Add(close);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 12), BackColor = Theme.Bg };
        body.Controls.Add(rulesHost);
        body.Controls.Add(bar);
        body.Controls.Add(rulesCaption);
        body.Controls.Add(seenHost);
        body.Controls.Add(seenCaption);
        body.Controls.Add(hintRow);
        body.Controls.Add(addRow);
        body.Controls.Add(modes);
        body.Controls.Add(intro);
        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = addButton;
        CancelButton = close;

        _seen.DrawItem += (_, e) => DrawRow(_seen, e, twoLines: true);
        _rules.DrawItem += (_, e) => DrawRow(_rules, e, twoLines: false);
        _pluginMode.Click += (_, _) => SetMode(HideRule.PluginKind);
        _wordsMode.Click += (_, _) => SetMode(HideRule.WordsKind);
        _seen.SelectedIndexChanged += (_, _) =>
        {
            if (_seen.SelectedItem is ValueTuple<string, int> p) { SetMode(HideRule.PluginKind); _value.Text = p.Item1; }
        };
        addButton.Click += (_, _) => AddFromBox();
        _remove.Click += (_, _) => RemoveSelected();
        _rules.SelectedIndexChanged += (_, _) => _remove.Enabled = _rules.SelectedIndex >= 0;
        close.Click += (_, _) => Close();

        FillSeen();
        FillRules();
        SetMode(HideRule.PluginKind);
    }

    private static Panel Framed(ListBox list)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(1) };
        host.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, host.Width - 1, host.Height - 1); };
        host.Controls.Add(list);
        DarkScroll.Apply(list);
        return host;
    }

    private void SetMode(string mode)
    {
        _mode = mode;
        var plugin = mode == HideRule.PluginKind;
        // The selected mode is the filled orange button; the other is the plain one.
        _pluginMode.BackColor = plugin ? Theme.Accent : Theme.Input;
        _pluginMode.ForeColor = plugin ? Color.White : Theme.Text;
        _wordsMode.BackColor = plugin ? Theme.Input : Theme.Accent;
        _wordsMode.ForeColor = plugin ? Theme.Text : Color.White;
        _value.Box.PlaceholderText = plugin ? "Plugin name, for example Economics" : "Words to hide, for example navmesh";
        _hint.Text = plugin
            ? "Hides every line that starts with that plugin's name in square brackets, such as [Economics] Saving balances for players..."
            : "Hides any line that contains these words anywhere in it, whichever plugin or part of the server wrote it.";
        _value.Box.Focus();
    }

    private void FillSeen()
    {
        _seen.Items.Clear();
        var hidden = _core.Settings.ConsoleHidden.Where(r => r.IsPlugin).Select(r => r.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ConsoleFilter.PluginsSeen(_core.Log.Entries).Where(p => !hidden.Contains(p.Name)).Take(30))
            _seen.Items.Add(p);
    }

    private void FillRules()
    {
        _rules.Items.Clear();
        foreach (var r in _core.Settings.ConsoleHidden) _rules.Items.Add(r);
        _remove.Enabled = false;
    }

    private void DrawRow(ListBox list, DrawItemEventArgs e, bool twoLines)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Theme.Selection : Theme.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (list.Items[e.Index] is ValueTuple<string, int> p)
        {
            TextRenderer.DrawText(e.Graphics, "[" + p.Item1 + "]", Theme.UI, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 140, e.Bounds.Height), Theme.Text, flags);
            TextRenderer.DrawText(e.Graphics, $"{p.Item2} line{(p.Item2 == 1 ? "" : "s")}", Theme.Small, new Rectangle(e.Bounds.Right - 120, e.Bounds.Y, 108, e.Bounds.Height), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }
        else
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index]?.ToString() ?? "", Theme.UI, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height), Theme.Text, flags);
    }

    private void AddFromBox()
    {
        if (ConsoleFilter.Add(_core.Settings, _mode, _value.Text))
        {
            _value.Text = "";
            FillSeen();
            FillRules();
            _core.NotifyConsoleFilterChanged();
        }
        _value.Box.Focus();
    }

    private void RemoveSelected()
    {
        if (_rules.SelectedItem is not HideRule rule) return;
        _core.Settings.ConsoleHidden.Remove(rule);
        _core.Settings.Save();
        FillSeen();
        FillRules();
        _core.NotifyConsoleFilterChanged();
    }
}
