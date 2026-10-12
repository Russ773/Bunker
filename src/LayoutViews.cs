using System.Drawing;

namespace Bunker;

/// <summary>The arranged widgets for the user's chosen layout. Editable = each slot has a picker; otherwise plain titles (fullscreen).</summary>
public class LayoutGrid : Panel
{
    private readonly Core _core;
    private readonly bool _editable;

    public LayoutGrid(Core core, bool editable)
    {
        _core = core;
        _editable = editable;
        BackColor = Theme.Bg;
        Rebuild();
    }

    public void Rebuild()
    {
        var layout = _core.Settings.Layout;
        if (layout.Slots is not { Length: >= 4 })
        {
            var fixedSlots = new WidgetKind[4];
            for (var i = 0; i < 4; i++) fixedSlots[i] = layout.SlotAt(i);
            layout.Slots = fixedSlots;
        }

        if (layout.CustomIds is not { Length: >= 4 }) layout.CustomIds = new string?[4];

        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }

        var preset = layout.Preset;
        var cols = preset is LayoutPreset.Single or LayoutPreset.TwoRows ? 1 : 2;
        var rows = preset is LayoutPreset.Single or LayoutPreset.TwoColumns ? 1 : 2;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, BackColor = Theme.Bg };
        for (var c = 0; c < cols; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (var r = 0; r < rows; r++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        var cells = PresetButton.Cells(preset);
        for (var i = 0; i < cells.Length; i++)
        {
            var (x, y, w, h) = cells[i];
            var card = MakeSlot(i, w >= 1f ? 6 : 2);
            card.Dock = DockStyle.Fill;
            table.Controls.Add(card, (int)Math.Round(x * cols), (int)Math.Round(y * rows));
            table.SetColumnSpan(card, Math.Max(1, (int)Math.Round(w * cols)));
            table.SetRowSpan(card, Math.Max(1, (int)Math.Round(h * rows)));
        }

        Controls.Add(table);
        ResumeLayout();
    }

    private Card MakeSlot(int index, int infoColumns)
    {
        var layout = _core.Settings.Layout;
        var kind = layout.SlotAt(index);
        var customId = layout.CustomAt(index);
        var title = kind == WidgetKind.Custom ? _core.Widgets.Get(customId)?.Name ?? "Missing widget" : Widgets.Name(kind);

        if (!_editable)
        {
            var (plain, plainActions) = Widgets.Create(kind, _core, infoColumns, customId);
            return Card.Titled(title, plain, plainActions);
        }

        var picker = Theme.GhostButton(title + "   ▾");
        picker.Width = 190;
        picker.Height = 30;
        picker.Location = new Point(0, 6);
        picker.Font = Theme.Semibold;

        void Choose(WidgetKind k, string? id)
        {
            layout.Slots[index] = k;
            if (layout.CustomIds is not { Length: >= 4 }) layout.CustomIds = new string?[4];
            layout.CustomIds[index] = id;
            _core.Settings.Save();
            BeginInvoke(Rebuild);
        }

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        void Heading(string text, bool separator)
        {
            if (separator) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false, BackColor = Theme.Input, ForeColor = Theme.Muted });
        }

        void AddKind(WidgetInfo info)
        {
            var item = new ToolStripMenuItem(info.Name) { Checked = info.Kind == kind, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) => Choose(info.Kind, null);
            menu.Items.Add(item);
        }

        Heading("CORE", false);
        foreach (var info in WidgetCatalog.CorePanels) AddKind(info);
        Heading("BUILT-IN WIDGETS", true);
        foreach (var info in WidgetCatalog.BuiltIn) AddKind(info);

        var community = _core.Widgets.All;
        if (community.Count > 0)
        {
            Heading("COMMUNITY WIDGETS", true);
            foreach (var d in community)
            {
                var def = d;
                var item = new ToolStripMenuItem(def.Name) { Checked = kind == WidgetKind.Custom && customId == def.Id, BackColor = Theme.Input, ForeColor = Theme.Text };
                item.Click += (_, _) => Choose(WidgetKind.Custom, def.Id);
                menu.Items.Add(item);
            }
        }
        picker.Click += (_, _) => menu.Show(picker, new Point(0, picker.Height));
        var holder = new Panel { Width = 200, BackColor = Theme.Panel };
        holder.Controls.Add(picker);

        var (content, actions) = Widgets.Create(kind, _core, infoColumns, customId);
        return new Card(holder, content, actions);
    }
}

public class LayoutPage : Panel
{
    private readonly Core _core;
    private readonly LayoutGrid _grid;
    private readonly List<PresetButton> _buttons = new();

    public LayoutPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        _grid = new LayoutGrid(core, editable: true);

        var fullscreen = Theme.GhostButton("Fullscreen");
        fullscreen.Font = new Font("Segoe UI Semibold", 10f);
        fullscreen.Height = 34;
        fullscreen.Width = 140;
        fullscreen.Margin = new Padding(0, 0, 18, 0);
        fullscreen.Click += (_, _) =>
        {
            using var f = new FullscreenForm(_core, FindForm());
            f.ShowDialog(FindForm());
        };

        var headerControls = new List<Control> { fullscreen };
        foreach (var preset in Enum.GetValues<LayoutPreset>())
        {
            var b = new PresetButton(preset);
            b.Click += (_, _) =>
            {
                _core.Settings.Layout.Preset = preset;
                _core.Settings.Save();
                _grid.Rebuild();
                Highlight();
            };
            _buttons.Add(b);
            headerControls.Add(b);
        }
        Highlight();

        var page = new Page("Layout", "Choose how many sections to show, then pick what goes in each: a core panel such as Console or Players, or any widget",
            _grid, headerControls.ToArray());
        page.Dock = DockStyle.Fill;
        Controls.Add(page);
    }

    private void Highlight()
    {
        foreach (var b in _buttons) { b.Selected = b.Preset == _core.Settings.Layout.Preset; b.Invalidate(); }
    }
}

/// <summary>The chosen layout shown across the whole screen. Esc, F11 or the Exit button returns to the app.</summary>
public class FullscreenForm : Form
{
    private readonly Core _core;
    private readonly Label _status = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted };

    public FullscreenForm(Core core, Form? owner)
    {
        _core = core;
        Theme.Apply(this);
        Text = "Bunker";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = (owner is not null ? Screen.FromControl(owner) : Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
        KeyPreview = true;
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") == "1") Location = new Point(-4000, 100);
#endif

        var bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, Padding = new Padding(16, 7, 16, 7) };
        var exit = new Button
        {
            Text = "", Font = new Font("Segoe MDL2 Assets", 11f), FlatStyle = FlatStyle.Flat, ForeColor = Theme.Muted,
            BackColor = Theme.Bg, Cursor = Cursors.Hand, Width = 38, Dock = DockStyle.Right, TabStop = false, UseVisualStyleBackColor = false,
        };
        exit.FlatAppearance.BorderSize = 0;
        exit.FlatAppearance.MouseOverBackColor = Theme.Err;
        exit.FlatAppearance.MouseDownBackColor = Theme.Err;
        exit.MouseEnter += (_, _) => exit.ForeColor = Color.White;
        exit.MouseLeave += (_, _) => exit.ForeColor = Theme.Muted;
        exit.Click += (_, _) => Close();
        Tips.Set(exit, "Exit fullscreen (Esc)");
        bar.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(16, 11) });
        _status.Location = new Point(110, 15);
        bar.Controls.Add(_status);
        bar.Controls.Add(exit);

        var grid = new LayoutGrid(core, editable: false) { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 6) };
        Controls.Add(grid);
        Controls.Add(bar);

        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F11) { e.Handled = true; Close(); } };
        _core.StateChanged += RefreshStatus;
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_SHOT") is { Length: > 0 } shot)
            Shown += async (_, _) => { await Task.Delay(1500); using var bmp = new Bitmap(Width, Height); DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(shot); };
#endif
        _core.ServerChanged += RefreshStatus;
        RefreshStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _core.StateChanged -= RefreshStatus; _core.ServerChanged -= RefreshStatus; }
        base.Dispose(disposing);
    }

    private void RefreshStatus()
    {
        var s = _core.Settings.ActiveServer;
        _status.Text = (s?.DisplayName ?? "No server") + "   -   " + _core.StatusText;
        _status.ForeColor = _core.State == ConnState.Connected ? Theme.Ok : Theme.Muted;
    }
}

public static class DarkScroll
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string? subIdList);

    /// <summary>Gives a control's native scrollbars the dark Windows look (Windows 10 1809 or later).</summary>
    public static void Apply(Control c)
    {
        void Set() { try { SetWindowTheme(c.Handle, Theme.IsLight ? "Explorer" : "DarkMode_Explorer", null); } catch { } }
        if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
    }
}
