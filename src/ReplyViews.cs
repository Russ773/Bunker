using System.Drawing;

namespace Bunker;

/// <summary>Somewhere a community widget can show the server's replies to its buttons.</summary>
public interface IReplyTarget
{
    Control Host { get; }
    void AppendCommand(string command);
    void AppendReply(string reply);
}

/// <summary>The default reply box: the text as it came, in the console font, with long lines kept whole and scrolled sideways.</summary>
public sealed class TextReplyBox : IReplyTarget
{
    private readonly RichTextBox _box;

    public Control Host { get; }

    public TextReplyBox(Core core, int height)
    {
        _box = new RichTextBox
        {
            ReadOnly = true, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = core.ConsoleFont,
            DetectUrls = false, HideSelection = false, Dock = DockStyle.Fill, Margin = new Padding(0),
            // Replies such as plugin tables are wide. Wrapping them splits every row in two, so lines stay whole and scroll sideways instead.
            WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both,
        };
        DarkScroll.Apply(_box);

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        var copy = new ToolStripMenuItem("Copy all") { BackColor = Theme.Input, ForeColor = Theme.Text };
        copy.Click += (_, _) => { if (_box.TextLength > 0) try { Clipboard.SetText(_box.Text); } catch { } };
        var clear = new ToolStripMenuItem("Clear") { BackColor = Theme.Input, ForeColor = Theme.Text };
        clear.Click += (_, _) => _box.Clear();
        menu.Items.Add(copy);
        menu.Items.Add(clear);
        _box.ContextMenuStrip = menu;

        var host = new Panel { Height = height + 22, BackColor = Theme.Bg, Padding = new Padding(10, 8, 6, 6), Margin = new Padding(0, 10, 0, 0) };
        host.Controls.Add(_box);
        Host = host;
    }

    public void AppendCommand(string command) => Append("> " + command, Theme.Accent);
    public void AppendReply(string reply) => Append(CustomWidgetView.CleanReply(reply), Theme.Text);

    private void Append(string text, Color color)
    {
        // A reply can arrive on a background thread; controls may only be touched from the UI thread.
        if (_box.InvokeRequired) { _box.BeginInvoke(() => Append(text, color)); return; }
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = color;
        _box.AppendText(text + Environment.NewLine);
        _box.ScrollToCaret();
    }
}

/// <summary>
/// Shows a plugin list (Carbon or Oxide) as a sortable table instead of a block of text. Click a heading to sort, click a row to
/// fill in the plugin name. Problems (plugins that were not loaded or would not compile) are listed underneath. If a reply is not a
/// plugin list it is shown as plain text, and the original text is always one click away.
/// </summary>
public sealed class PluginTableView : Panel, IReplyTarget
{
    private readonly TextReplyBox _raw;
    private readonly DarkGrid _grid = new();
    private readonly Label _summary = new() { AutoSize = false, ForeColor = Theme.Muted, Font = Theme.Small, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _toggle = new() { AutoSize = false, Width = 110, ForeColor = Theme.Accent, Font = Theme.Small, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, Cursor = Cursors.Hand };
    private readonly RichTextBox _problems = new()
    {
        ReadOnly = true, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Small, DetectUrls = false,
        WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both, Dock = DockStyle.Bottom, Height = 0, Visible = false,
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    private PluginList? _list;
    private bool _showingRaw = true;
    private bool _filling;

    public Control Host => this;

    /// <summary>Called with the plugin's name when a row is clicked.</summary>
    public Action<string>? Picked { get; set; }

    public PluginTableView(Core core, int height)
    {
        _raw = new TextReplyBox(core, height);
        Height = height + 56;
        BackColor = Theme.Bg;
        Padding = new Padding(10, 6, 6, 6);
        Margin = new Padding(0, 10, 0, 0);

        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.RowTemplate.Height = 28;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.Dock = DockStyle.Fill;
        _grid.SortCompare += OnSortCompare;
        _grid.SelectionChanged += (_, _) =>
        {
            if (_filling || _list is null || _grid.SelectedRows.Count != 1) return;
            if (_grid.SelectedRows[0].Cells[_list.NameColumn].Value is string name && name.Length > 0) Picked?.Invoke(name);
        };

        var bar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.Bg };
        bar.Controls.Add(_summary);
        bar.Controls.Add(_toggle);
        _toggle.Click += (_, _) => { if (_list is not null) Show(raw: !_showingRaw); };

        _raw.Host.Dock = DockStyle.Fill;
        _raw.Host.Margin = new Padding(0);
        // Only one of the raw text and the table is ever in the layout, so they can never draw over each other.
        Controls.Add(_content);
        Controls.Add(_problems);
        Controls.Add(bar);
        DarkScroll.Apply(_problems);

        Show(raw: true);
        _summary.Text = "Press a button to load the list.";
        _toggle.Text = "";
    }

    public void AppendCommand(string command)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendCommand(command)); return; }
        _raw.AppendCommand(command);
    }

    public void AppendReply(string reply)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendReply(reply)); return; }
        _raw.AppendReply(reply);
        _list = PluginListParser.TryParse(CustomWidgetView.CleanReply(reply));
        if (_list is null) { Show(raw: true); _summary.Text = "Not a plugin list, so it is shown as text."; _toggle.Text = ""; return; }
        FillTable(_list);
        Show(raw: false);
    }

    private void Show(bool raw)
    {
        _showingRaw = raw;
        _content.Controls.Clear();
        _content.Controls.Add(raw ? _raw.Host : _grid);
        _toggle.Text = _list is null ? "" : raw ? "Show table" : "Show raw reply";
        if (_list is not null) _summary.Text = _list.Summary + (raw ? "" : "   (click a heading to sort, click a row to pick it)");
    }

    private void FillTable(PluginList list)
    {
        _filling = true;
        _grid.SuspendLayout();
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        foreach (var name in list.Columns) _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, SortMode = DataGridViewColumnSortMode.Automatic, MinimumWidth = 60 });
        foreach (var row in list.Rows) _grid.Rows.Add(row.Cast<object>().ToArray());
        _grid.Columns[list.NameColumn].DefaultCellStyle.Font = Theme.Semibold;
        // Type and version columns read better in a quieter colour than the plugin's name.
        if (list.Columns[0] == "Type") _grid.Columns[0].DefaultCellStyle.ForeColor = Theme.Muted;
        // Size each column from its longest text (measured here because the grid may not have been drawn yet). Columns then share
        // any spare width in proportion, and never get narrower than their text, so a table that fits has no sideways scrollbar.
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            var font = c.DefaultCellStyle.Font ?? _grid.DefaultCellStyle.Font ?? Theme.UI;
            var widest = TextRenderer.MeasureText(c.HeaderText, Theme.Small).Width + 30; // room for the sort arrow
            foreach (DataGridViewRow row in _grid.Rows)
                widest = Math.Max(widest, TextRenderer.MeasureText(Convert.ToString(row.Cells[c.Index].Value) ?? "", font).Width);
            var wide = Math.Clamp(widest + 26, 70, 360);
            c.MinimumWidth = wide;
            c.FillWeight = wide;
        }
        _grid.ClearSelection();
        _grid.ResumeLayout();
        _filling = false;
        ShowProblems(list);
    }

    private void ShowProblems(PluginList list)
    {
        _problems.Clear();
        if (list.Unloaded.Count == 0 && list.Failed.Count == 0) { _problems.Visible = false; _problems.Height = 0; return; }

        var lines = 0;
        void Write(string text, Color colour, bool bold = false)
        {
            _problems.SelectionStart = _problems.TextLength;
            _problems.SelectionColor = colour;
            _problems.SelectionFont = new Font(Theme.Small, bold ? FontStyle.Bold : FontStyle.Regular);
            _problems.AppendText(text + Environment.NewLine);
            lines++;
        }

        if (list.Unloaded.Count > 0) Write("Not loaded:  " + string.Join(",  ", list.Unloaded), Theme.Warn, bold: true);
        foreach (var file in list.Failed.GroupBy(f => f.File))
        {
            Write($"Would not compile:  {file.Key}  ({file.Count()} error{(file.Count() == 1 ? "" : "s")})", Theme.Err, bold: true);
            foreach (var f in file) Write($"      line {f.Line}   {f.Message}", Theme.Muted);
        }
        _problems.BackColor = Theme.Bg;
        _problems.Height = Math.Min(lines * 19 + 14, 92); // a few lines; the rest scrolls, so the table keeps its room
        _problems.Visible = true;
    }

    /// <summary>Sorts numbers and sizes by value ("9ms" before "1092ms") and everything else as text.</summary>
    private static void OnSortCompare(object? sender, DataGridViewSortCompareEventArgs e)
    {
        var a = Convert.ToString(e.CellValue1) ?? "";
        var b = Convert.ToString(e.CellValue2) ?? "";
        var x = PluginListParser.SortValue(a);
        var y = PluginListParser.SortValue(b);
        e.SortResult = x is not null && y is not null ? x.Value.CompareTo(y.Value)
            : x is not null ? 1
            : y is not null ? -1
            : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        e.Handled = true;
    }
}
