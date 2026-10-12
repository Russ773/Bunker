using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace Bunker;

/// <summary>The Widgets page section for managing community widgets: add from a file or link, browse the library, review, remove.</summary>
public class WidgetManagerView : Panel
{
    private readonly Core _core;
    private readonly DarkGrid _grid = new();
    private readonly Button _details = Theme.GhostButton("Details");
    private readonly Button _remove = Theme.GhostButton("Remove");
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, MaximumSize = new Size(640, 0) };

    public WidgetManagerView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        var intro = new Label
        {
            Text = "Community widgets add extra buttons and fields to Bunker. A widget is a small JSON file, never a program, and it can only send the commands you see listed when you install it.",
            Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0),
        };

        var browse = Theme.PrimaryButton("Browse library");
        var file = Theme.GhostButton("Add from file...");
        var link = Theme.GhostButton("Add from link...");
        var folder = Theme.GhostButton("Open folder");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Panel };
        foreach (var b in new[] { browse, file, link, folder }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); top.Controls.Add(b); }

        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("Author", "Author");
        _grid.Columns.Add("Version", "Version");
        _grid.Columns.Add("Commands", "Commands");
        _grid.Columns[0].FillWeight = 120;
        _grid.Columns[1].FillWeight = 90;
        _grid.Columns[2].FillWeight = 50;
        _grid.Columns[3].FillWeight = 60;
        _grid.Dock = DockStyle.Fill;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Panel, WrapContents = true };
        foreach (var b in new[] { _details, _remove }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); bottom.Controls.Add(b); }
        bottom.Controls.Add(_status);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(intro);

        browse.Click += (_, _) => { using var f = new WidgetLibraryForm(this); f.ShowDialog(FindForm()); };
        file.Click += async (_, _) => await AddFromFileAsync();
        link.Click += async (_, _) => await AddFromLinkAsync();
        folder.Click += (_, _) => { try { Directory.CreateDirectory(_core.Widgets.Folder); Process.Start(new ProcessStartInfo(_core.Widgets.Folder) { UseShellExecute = true }); } catch { } };
        _details.Click += (_, _) => { if (Selected() is { } d) { using var f = new WidgetReviewForm(d, install: false); f.ShowDialog(FindForm()); } };
        _remove.Click += (_, _) => RemoveSelected();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) _details.PerformClick(); };
        _core.Widgets.Changed += Fill;
        Fill();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Widgets.Changed -= Fill;
        base.Dispose(disposing);
    }

    /// <summary>True if this widget is already installed and the offered copy is not a newer version.</summary>
    public bool IsUpToDate(WidgetDef def) => _core.Widgets.Get(def.Id) is { } have && !IsNewer(def.Version, have.Version);

    /// <summary>True if this widget is installed but the offered copy is a newer version.</summary>
    public bool IsUpdate(WidgetDef def) => _core.Widgets.Get(def.Id) is { } have && IsNewer(def.Version, have.Version);

    private static bool IsNewer(string? offered, string? installed) =>
        Version.TryParse(offered, out var a) && Version.TryParse(installed, out var b) && a > b;

    private WidgetDef? Selected() => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as WidgetDef : null;

    private void UpdateButtons() => _details.Enabled = _remove.Enabled = Selected() is not null;

    private void Fill()
    {
        _grid.Rows.Clear();
        foreach (var d in _core.Widgets.All)
        {
            var i = _grid.Rows.Add(d.Name, d.Author ?? "", d.Version ?? "", WidgetRules.AllCommands(d).Distinct().Count());
            _grid.Rows[i].Tag = d;
        }
        UpdateButtons();
        if (_grid.Rows.Count == 0) Say("No community widgets installed yet. Use Browse library to get started.", Theme.Muted);
    }

    private void Say(string text, Color color) { _status.Text = text; _status.ForeColor = color; }

    private async Task AddFromFileAsync()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a widget file", Filter = "Bunker widget (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > WidgetRules.MaxFileBytes) { Say("That file is larger than 64 KB.", Theme.Err); return; }
            Review(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception e) { Say("Could not read the file: " + e.Message, Theme.Err); }
    }

    private async Task AddFromLinkAsync()
    {
        using var prompt = new LinkPromptForm();
        if (prompt.ShowDialog(FindForm()) != DialogResult.OK || prompt.Url is not { Length: > 0 } url) return;
        Say("Downloading...", Theme.Muted);
        try { Review(await WidgetNet.GetAsync(url)); }
        catch (Exception e) { Say("Could not download the widget: " + e.Message, Theme.Err); }
    }

    /// <summary>Checks a widget file and, if it is acceptable, shows everything it can do before installing.</summary>
    public bool Review(string json)
    {
        var def = WidgetRules.Parse(json, out var errors);
        if (def is null)
        {
            MessageBox.Show(FindForm(), "This widget can't be installed:\n\n- " + string.Join("\n- ", errors), "Widget not accepted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        using var f = new WidgetReviewForm(def, install: true);
        if (f.ShowDialog(FindForm()) != DialogResult.OK) return false;
        _core.Widgets.Install(def, json);
        Say($"Installed \"{def.Name}\". It is now in the Widgets list and in the Layout widget picker.", Theme.Ok);
        return true;
    }

    private void RemoveSelected()
    {
        if (Selected() is not { } d) return;
        var ok = MessageBox.Show(FindForm(), $"Remove the widget \"{d.Name}\"?\n\nIf it is used in your Layout, that section will show a notice.",
            "Remove widget", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok == DialogResult.Yes) _core.Widgets.Remove(d.Id);
    }
}

/// <summary>Shows what a widget is and every command it can send. Install is only possible if nothing is blocked.</summary>
public class WidgetReviewForm : Form
{
    public WidgetReviewForm(WidgetDef def, bool install)
    {
        Theme.Apply(this);
        Text = install ? "Install widget" : "Widget details";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 620);

        var commands = WidgetRules.AllCommands(def).Distinct().ToList();
        var warnings = commands.Count(c => WidgetRules.RiskOf(c) == CommandRisk.Warning);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 22, 28, 14) };
        flow.Controls.Add(new Label { Text = def.Name, Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, MaximumSize = new Size(540, 0) });
        flow.Controls.Add(new Label { Text = $"by {(string.IsNullOrWhiteSpace(def.Author) ? "unknown author" : def.Author)}   -   version {def.Version ?? "?"}", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 2, 0, 10) });
        if (!string.IsNullOrWhiteSpace(def.Description))
            flow.Controls.Add(new Label { Text = def.Description, AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 10) });

        flow.Controls.Add(new Label
        {
            Text = commands.Count == 0 ? "This widget does not send any commands." : $"This widget can send these {commands.Count} command{(commands.Count == 1 ? "" : "s")} to your server:",
            ForeColor = Theme.Text, Font = Theme.Semibold, AutoSize = true, Margin = new Padding(0, 6, 0, 6),
        });

        var list = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 544, Height = 230, BackColor = Theme.Bg,
            ForeColor = Theme.Text, Font = Theme.Mono, BorderStyle = BorderStyle.FixedSingle,
            Text = string.Join(Environment.NewLine, commands.Select(c => (WidgetRules.RiskOf(c) == CommandRisk.Warning ? "!  " : "   ") + c)),
        };
        DarkScroll.Apply(list);
        list.TabStop = false;
        Shown += (_, _) => list.Select(0, 0); // no pre-selected text
        flow.Controls.Add(list);

        var note = warnings > 0
            ? $"{warnings} command{(warnings == 1 ? "" : "s")} marked with ! can affect players or the server itself (for example kick, ban, restart or give-to-everyone). Only install this widget if you trust its author."
            : "Words in {braces} are filled in from the widget's fields when you press a button. Nothing else is ever sent.";
        flow.Controls.Add(new Label { Text = note, ForeColor = warnings > 0 ? Theme.Warn : Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 10, 0, 0) });

        var bar = new FlowLayoutPanel { Width = 544, Height = 54, Margin = new Padding(0, 14, 0, 0), BackColor = Theme.Bg };
        var ok = Theme.PrimaryButton(install ? "Install widget" : "Close");
        var cancel = Theme.GhostButton("Cancel");
        ok.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(ok);
        if (install) bar.Controls.Add(cancel);
        flow.Controls.Add(bar);

        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = ok;
        CancelButton = install ? cancel : ok;
        ok.Click += (_, _) => { DialogResult = install ? DialogResult.OK : DialogResult.Cancel; Close(); };
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") == "1") { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); }
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_SHOT") is { Length: > 0 } shot)
            Shown += async (_, _) => { await Task.Delay(800); using var bmp = new Bitmap(Width, Height); DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(shot); Close(); };
#endif
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}

/// <summary>Asks for a link (https) to a widget file.</summary>
public class LinkPromptForm : Form
{
    private readonly ModernField _url = new();
    public string? Url { get; private set; }

    public LinkPromptForm()
    {
        Theme.Apply(this);
        Text = "Add widget from a link";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 230);

        var ok = Theme.PrimaryButton("Continue");
        var cancel = Theme.GhostButton("Cancel");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 24, 28, 16) };
        flow.Controls.Add(new Label { Text = "Link to the widget (.json) file", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true });
        _url.Width = 464; _url.Margin = new Padding(0, 6, 0, 0); _url.Box.PlaceholderText = "https://raw.githubusercontent.com/...";
        flow.Controls.Add(_url);
        flow.Controls.Add(new Label { Text = "You will see everything the widget can do before anything is installed.", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(464, 0), Margin = new Padding(0, 12, 0, 0) });
        var bar = new FlowLayoutPanel { Width = 464, Height = 48, Margin = new Padding(0, 16, 0, 0), BackColor = Theme.Bg };
        ok.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(ok); bar.Controls.Add(cancel);
        flow.Controls.Add(bar);
        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = ok; CancelButton = cancel;
        ok.Click += (_, _) => { Url = _url.Text.Trim(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }
}

/// <summary>The community library: widget files published in the project's widgets folder on GitHub.</summary>
public class WidgetLibraryForm : Form
{
    private readonly WidgetManagerView _manager;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, IntegralHeight = false, ItemHeight = 28, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly Label _detail = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Theme.Text, Padding = new Padding(16, 6, 6, 6) };
    private readonly Button _install = Theme.PrimaryButton("Review and install");
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<(string Name, string Url, string Json, WidgetDef? Def)> _items = new();

    public WidgetLibraryForm(WidgetManagerView manager)
    {
        _manager = manager;
        Theme.Apply(this);
        Text = "Community widget library";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 480);
        MinimumSize = new Size(600, 400);

        var left = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(16, 16, 0, 16) };
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(1) };
        listHost.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, listHost.Width - 1, listHost.Height - 1); };
        listHost.Controls.Add(_list);
        left.Controls.Add(listHost);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(16, 12, 16, 12) };
        _install.Dock = DockStyle.Right;
        _install.Width = 170;
        _install.Enabled = false;
        bottom.Controls.Add(_status);
        bottom.Controls.Add(_install);

        Controls.Add(_detail);
        Controls.Add(left);
        Controls.Add(bottom);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        DarkScroll.Apply(_list);

        _list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(selected ? Theme.Selection : Theme.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, _list.Items[e.Index]?.ToString() ?? "", Theme.UI, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _install.Click += (_, _) => { if (_list.SelectedIndex >= 0 && _manager.Review(_items[_list.SelectedIndex].Json)) Close(); };
        Shown += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _status.Text = "Loading the library...";
        try
        {
            // A plain index file, served from raw.githubusercontent.com, so browsing is not limited by GitHub's API rate limit.
            var baseUrl = $"https://raw.githubusercontent.com/{Updater.Repo}/main/widgets/";
            using var doc = JsonDocument.Parse(await WidgetNet.GetAsync(baseUrl + "index.json"));
            var files = doc.RootElement.GetProperty("widgets").EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^[a-z0-9][a-z0-9_-]*\.json$"))
                .Select(n => (Name: n, Url: baseUrl + n)).ToList();

            var already = 0;
            foreach (var (name, url) in files)
            {
                try
                {
                    var json = await WidgetNet.GetAsync(url);
                    var def = WidgetRules.Parse(json, out _);
                    if (def is not null && _manager.IsUpToDate(def)) { already++; continue; } // installed already: nothing to offer
                    _items.Add((name, url, json, def));
                    _list.Items.Add(def is null ? name + "  (not valid)" : _manager.IsUpdate(def) ? def.Name + "  (update)" : def.Name);
                }
                catch { /* skip files that cannot be fetched */ }
            }
            _status.Text = _items.Count == 0
                ? (already > 0 ? "You already have every widget in the library." : "The library is empty right now.")
                : $"{_items.Count} widget{(_items.Count == 1 ? "" : "s")} available" + (already > 0 ? $" ({already} you already have are not shown)." : ".");
            if (_items.Count > 0) _list.SelectedIndex = 0;
        }
        catch (Exception e) { _status.Text = "Could not load the library: " + e.Message; }
    }

    private void ShowSelected()
    {
        if (_list.SelectedIndex < 0) { _install.Enabled = false; return; }
        var item = _items[_list.SelectedIndex];
        if (item.Def is null) { _detail.Text = "This file is not a valid widget."; _install.Enabled = false; return; }
        var d = item.Def;
        var count = WidgetRules.AllCommands(d).Distinct().Count();
        _detail.Text = $"{d.Name}\n\nby {(string.IsNullOrWhiteSpace(d.Author) ? "unknown author" : d.Author)}  -  version {d.Version ?? "?"}\n\n{d.Description}\n\nSends {count} different command{(count == 1 ? "" : "s")}. You review them before installing.";
        _install.Enabled = true;
    }
}
