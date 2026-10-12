using System.Drawing;

namespace Bunker;

/// <summary>The Console tab in Settings: choose the console font and size, with a live preview.</summary>
public partial class SettingsPage
{
    private const float DefaultSize = 10.5f;
    private readonly ListBox _fontList = new()
    {
        Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30, BorderStyle = BorderStyle.None,
        BackColor = Theme.Input, ForeColor = Theme.Text, IntegralHeight = false,
    };
    private readonly Label _sizeLabel = new() { AutoSize = false, Width = 90, Height = 36, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Text, Font = Theme.Semibold };
    private readonly RichTextBox _preview = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text,
        DetectUrls = false, TabStop = false, Cursor = Cursors.Default,
    };
    private bool _fontsLoaded;
    private bool _loadingFonts;
    private bool _showAllFonts;

    private Control BuildConsole()
    {
        var inner = new Panel { BackColor = Theme.Panel };

        // Left: font list
        var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(0, 0, 18, 0), BackColor = Theme.Panel };
        var showAll = new CheckBox { Text = "Show all fonts, not just fixed-width ones", Dock = DockStyle.Bottom, Height = 30, ForeColor = Theme.Muted, Font = Theme.Small, Cursor = Cursors.Hand };
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(1) };
        listHost.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, listHost.Width - 1, listHost.Height - 1); };
        listHost.Controls.Add(_fontList);
        left.Controls.Add(listHost);
        left.Controls.Add(showAll);
        left.Controls.Add(new Label { Text = "Font", Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small });
        DarkScroll.Apply(_fontList);

        // Right: size controls and the preview
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        var preview = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(14, 12, 10, 10) };
        preview.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, preview.Width - 1, preview.Height - 1); };
        preview.Controls.Add(_preview);

        var sizeRow = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Panel };
        sizeRow.Controls.Add(new Label { Text = "Size", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Location = new Point(0, 0) });
        var smaller = Theme.GhostButton("−");
        var bigger = Theme.GhostButton("+");
        var reset = Theme.GhostButton("Reset to default");
        smaller.Width = bigger.Width = 44;
        smaller.Location = new Point(0, 26);
        _sizeLabel.Location = new Point(50, 26);
        bigger.Location = new Point(144, 26);
        reset.Location = new Point(204, 26);
        sizeRow.Controls.AddRange(new Control[] { smaller, _sizeLabel, bigger, reset });

        var previewCaption = new Label { Text = "Preview", Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0) };
        right.Controls.Add(preview);
        right.Controls.Add(previewCaption);
        right.Controls.Add(sizeRow);

        inner.Controls.Add(right);
        inner.Controls.Add(left);

        // Behaviour
        inner.VisibleChanged += (_, _) => { if (inner.Visible && !_fontsLoaded) LoadFonts(); };
        _fontList.DrawItem += DrawFontItem;
        _fontList.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingFonts || _fontList.SelectedItem is not string name) return;
            _core.SetConsoleFont(name, _core.Settings.ConsoleFontSize);
        };
        smaller.Click += (_, _) => _core.SetConsoleFont(_core.Settings.ConsoleFontName, _core.Settings.ConsoleFontSize - 1);
        bigger.Click += (_, _) => _core.SetConsoleFont(_core.Settings.ConsoleFontName, _core.Settings.ConsoleFontSize + 1);
        reset.Click += (_, _) => { _core.SetConsoleFont("Consolas", DefaultSize); SelectCurrentFont(); };
        showAll.CheckedChanged += (_, _) => { _showAllFonts = showAll.Checked; LoadFonts(); };
        _core.ConsoleFontChanged += RefreshPreview;
        _preview.HandleCreated += (_, _) => _preview.BeginInvoke(new Action(RefreshPreview)); // formatting only sticks once the control is fully created
        RefreshPreview();
        return Wrap("Console", inner);
    }

    private void LoadFonts()
    {
        _loadingFonts = true;
        _fontList.BeginUpdate();
        _fontList.Items.Clear();
        var current = _core.Settings.ConsoleFontName;
        var names = FontNames(_showAllFonts);
        if (!names.Contains(current, StringComparer.OrdinalIgnoreCase)) names.Add(current);
        foreach (var n in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) _fontList.Items.Add(n);
        _fontList.EndUpdate();
        _fontsLoaded = true;
        _loadingFonts = false;
        SelectCurrentFont();
    }

    private void SelectCurrentFont()
    {
        _loadingFonts = true;
        var i = _fontList.Items.Cast<string>().ToList().FindIndex(n => n.Equals(_core.Settings.ConsoleFontName, StringComparison.OrdinalIgnoreCase));
        _fontList.SelectedIndex = i;
        if (i >= 0) _fontList.TopIndex = Math.Max(0, i - 3);
        _loadingFonts = false;
    }

    private static readonly string[] IconFontHints = { "Symbol", "dings", "MDL2", "Fluent Icons", "Marlett", "Emoji", "Icons" };

    private static bool IconFont(string name) => IconFontHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Installed font families. By default only fixed-width ones (every letter the same width), which suit a console.</summary>
    private static List<string> FontNames(bool all)
    {
        var names = new List<string>();
        var size = new Size(int.MaxValue, int.MaxValue);
        foreach (var family in FontFamily.Families)
        {
            if (!family.IsStyleAvailable(FontStyle.Regular) || IconFont(family.Name)) continue;
            if (!all)
            {
                try
                {
                    using var f = new Font(family, 12f);
                    var narrow = TextRenderer.MeasureText("iiiiiiiiii", f, size, TextFormatFlags.NoPadding).Width;
                    var wide = TextRenderer.MeasureText("WWWWWWWWWW", f, size, TextFormatFlags.NoPadding).Width;
                    if (Math.Abs(narrow - wide) > 2) continue;
                }
                catch { continue; }
            }
            names.Add(family.Name);
        }
        return names;
    }

    private void DrawFontItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _fontList.Items[e.Index] is not string name) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Theme.Selection : Theme.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
        Font font;
        try { font = new Font(name, 11f); } catch { font = Theme.UI; }
        TextRenderer.DrawText(e.Graphics, name, font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (!ReferenceEquals(font, Theme.UI)) font.Dispose();
    }

    /// <summary>Shows sample console lines (in the real colours) using the chosen font and size.</summary>
    private void RefreshPreview()
    {
        _sizeLabel.Text = $"{_core.Settings.ConsoleFontSize:0.#} pt";

        // Built as one formatted block (RTF) so the colours apply even before the control has a window.
        var colors = new List<Color> { Theme.Dim };
        int Colour(Color c) { var i = colors.IndexOf(c); if (i < 0) { colors.Add(c); i = colors.Count - 1; } return i + 1; }
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

        var body = new System.Text.StringBuilder();
        void Line(string time, string text, LogKind kind) =>
            body.Append($"\\cf1 {time}  \\cf{Colour(ConsoleView.ColorOf(kind))} {Esc(text)}\\par ");
        Line("12:00:01", "Connected to My Server (203.0.113.10:28016)", LogKind.Info);
        Line("12:00:02", "Server started. Welcome to the island!", LogKind.Normal);
        Line("12:00:07", "[Warning] Slow tick detected on thread 4", LogKind.Warning);
        Line("12:00:09", "[Error] Missing item definition: scrap_old", LogKind.Error);
        Line("12:00:12", "Ironclad: anyone near the airfield?", LogKind.Chat);
        Line("12:00:15", "> status", LogKind.Command);

        var table = string.Concat(colors.Select(c => $"\\red{c.R}\\green{c.G}\\blue{c.B};"));
        var font = Esc(_core.Settings.ConsoleFontName);
        var halfPoints = (int)Math.Round(_core.Settings.ConsoleFontSize * 2);
        if (!_preview.IsHandleCreated) return;
        _preview.Rtf = $"{{\\rtf1\\ansi\\deff0{{\\fonttbl{{\\f0\\fmodern\\fcharset0 {font};}}}}{{\\colortbl;{table}}}\\f0\\fs{halfPoints} {body}}}";
    }
}
