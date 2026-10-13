using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>A bordered panel with a header (title or custom control on the left, optional actions on the right).</summary>
public class Card : Panel
{
    public Card(Control headerLeft, Control content, params Control[] actions)
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        Padding = new Padding(1);
        Margin = new Padding(6);

        var header = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(14, 0, 10, 0), BackColor = Theme.Panel };
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent,
        };
        right.Controls.AddRange(actions);
        headerLeft.Dock = DockStyle.Left;
        header.Controls.Add(headerLeft);
        header.Controls.Add(right);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 12), BackColor = Theme.Panel };
        content.Dock = DockStyle.Fill;
        body.Controls.Add(content);

        Controls.Add(body);
        Controls.Add(header);
    }

    public static Card Titled(string title, Control content, params Control[] actions) =>
        new(new Label { Text = title, Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true, Padding = new Padding(0, 11, 0, 0) },
            content, actions);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>A page: a big title (and optional right-hand controls) above its content.</summary>
public class Page : Panel
{
    public Page(string title, string subtitle, Control body, params Control[] headerRight)
    {
        BackColor = Theme.Bg;
        Padding = new Padding(22, 18, 22, 14);

        var head = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Theme.Bg };
        head.Controls.Add(new Label { Text = subtitle, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 36) });
        head.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 17f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0) });
        if (headerRight.Length > 0)
        {
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
            right.Controls.AddRange(headerRight);
            head.Controls.Add(right);
        }

        body.Dock = DockStyle.Fill;
        Controls.Add(body);
        Controls.Add(head);
    }
}

/// <summary>A sidebar navigation item with an icon glyph, hover highlight and an accent bar when selected.</summary>
public class NavButton : Control
{
    private static readonly Font IconFont = new("Segoe MDL2 Assets", 13f);
    private readonly string _glyph;
    private bool _hover;
    private bool _selected;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public NavButton(string glyph, string text)
    {
        _glyph = glyph;
        Text = text;
        Height = 38;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        Font = Theme.UI;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);
        var r = new Rectangle(10, 3, Width - 20, Height - 6);
        if (_selected || _hover)
        {
            using var path = Rounded(r, 8);
            using var br = new SolidBrush(_selected ? Theme.Input : Theme.Hover);
            g.FillPath(br, path);
        }
        if (_selected)
        {
            using var br = new SolidBrush(Theme.Accent);
            g.FillRectangle(br, 0, 10, 3, Height - 20);
        }
        var fore = _selected ? Theme.Text : Theme.Muted;
        TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(20, 0, 30, Height), _selected ? Theme.Accent : fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(56, 0, Width - 60, Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>A small button showing a miniature of a layout preset.</summary>
public class PresetButton : Control
{
    private bool _hover;
    public LayoutPreset Preset { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public PresetButton(LayoutPreset preset)
    {
        Preset = preset;
        Size = new Size(46, 34);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    /// <summary>Cells as fractions of a unit square: x, y, w, h.</summary>
    public static (float X, float Y, float W, float H)[] Cells(LayoutPreset p) => p switch
    {
        LayoutPreset.Single => new[] { (0f, 0f, 1f, 1f) },
        LayoutPreset.TwoColumns => new[] { (0f, 0f, .5f, 1f), (.5f, 0f, .5f, 1f) },
        LayoutPreset.TwoRows => new[] { (0f, 0f, 1f, .5f), (0f, .5f, 1f, .5f) },
        LayoutPreset.ThreeLeftBig => new[] { (0f, 0f, .5f, 1f), (.5f, 0f, .5f, .5f), (.5f, .5f, .5f, .5f) },
        _ => new[] { (0f, 0f, .5f, .5f), (.5f, 0f, .5f, .5f), (0f, .5f, .5f, .5f), (.5f, .5f, .5f, .5f) },
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);
        var box = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = NavButton.Rounded(box, 6))
        {
            using var fill = new SolidBrush(Selected ? Theme.Input : Theme.Panel);
            using var pen = new Pen(Selected ? Theme.Accent : (_hover ? Theme.Muted : Theme.Border), Selected ? 1.6f : 1f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        var inner = new RectangleF(10, 8, Width - 21, Height - 17);
        using var cell = new SolidBrush(Selected ? Theme.Accent : Theme.Muted);
        foreach (var (x, y, w, h) in Cells(Preset))
            g.FillRectangle(cell, inner.X + x * inner.Width + 1.5f, inner.Y + y * inner.Height + 1.5f, w * inner.Width - 3f, h * inner.Height - 3f);
    }
}

/// <summary>Dark colours for context menus so they match the rest of the app.</summary>
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Input;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.Selection;
        public override Color ImageMarginGradientBegin => Theme.Input;
        public override Color ImageMarginGradientMiddle => Theme.Input;
        public override Color ImageMarginGradientEnd => Theme.Input;
        public override Color CheckBackground => Theme.Selection;
        public override Color CheckSelectedBackground => Theme.Selection;
        public override Color CheckPressedBackground => Theme.Selection;
    }
}

/// <summary>One dark tooltip used everywhere, so long (truncated) names can be read in full on hover.</summary>
public static class Tips
{
    private static readonly ToolTip Tip = Create();

    private static ToolTip Create()
    {
        var t = new ToolTip { OwnerDraw = true, InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 12000 };
        t.Popup += (_, e) =>
        {
            var size = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl), Theme.UI, new Size(520, 0), TextFormatFlags.WordBreak);
            e.ToolTipSize = new Size(size.Width + 20, size.Height + 14);
        };
        t.Draw += (_, e) =>
        {
            using (var bg = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.UI, new Rectangle(10, 7, e.Bounds.Width - 20, e.Bounds.Height - 14),
                Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.Left);
        };
        return t;
    }

    public static void Set(Control c, string? text) => Tip.SetToolTip(c, string.IsNullOrWhiteSpace(text) ? null : text);
}

/// <summary>A read-only, dark-themed table (used for players and servers).</summary>
public class DarkGrid : DataGridView
{
    public DarkGrid()
    {
        ReadOnly = true;
        AllowUserToAddRows = AllowUserToDeleteRows = AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        EnableHeadersVisualStyles = false;
        BackgroundColor = Theme.Panel;
        GridColor = Theme.GridLine;
        RowTemplate.Height = 32;
        ColumnHeadersHeight = 34;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Muted, Font = Theme.Small, SelectionBackColor = Theme.Panel, SelectionForeColor = Theme.Muted,
        };
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Text,
            Padding = new Padding(4, 0, 4, 0),
        };
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        DarkScroll.Apply(this);
        // A table's scrollbars are separate child controls, so they need the dark look applied individually.
        HandleCreated += (_, _) => { foreach (var bar in Controls.OfType<ScrollBar>()) DarkScroll.Apply(bar); };
    }
}

/// <summary>A horizontal tab: icon glyph and text, with an accent underline when selected.</summary>
public class TabButton : Control
{
    private static readonly Font IconFont = new("Segoe MDL2 Assets", 12f);
    private readonly string _glyph;
    private bool _hover;
    private bool _selected;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public TabButton(string glyph, string text)
    {
        _glyph = glyph;
        Text = text;
        Font = Theme.UI;
        Height = 46;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
        Width = 56 + TextRenderer.MeasureText(text, Theme.UI).Width;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_hover && !_selected ? Theme.Hover : Theme.Panel);
        var fore = _selected ? Theme.Text : Theme.Muted;
        TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(8, 0, 28, Height - 3), _selected ? Theme.Accent : fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(38, 0, Width - 40, Height - 3), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (_selected)
        {
            using var br = new SolidBrush(Theme.Accent);
            g.FillRectangle(br, 6, Height - 3, Width - 12, 3);
        }
    }
}

/// <summary>A flat button painted by Bunker so disabled buttons show clearly in grey (the system look is almost invisible on a dark theme).</summary>
public class ThemedButton : Button
{
    private bool _hover;
    private bool _down;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color HoverBack { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BorderSize { get; set; }

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var disabled = !Enabled;
        var back = disabled ? Theme.DisabledBack : (_hover || _down) ? HoverBack : BackColor;
        var fore = disabled ? Theme.DisabledFore : ForeColor;
        g.Clear(back);

        if (disabled || BorderSize > 0)
        {
            using var pen = new Pen(disabled ? Theme.Border : BorderColor);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        else if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Text) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
        }

        var left = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix
            | (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        var area = left ? new Rectangle(12, 0, Width - 20, Height) : new Rectangle(4, 0, Width - 8, Height);
        TextRenderer.DrawText(g, Text, Font, area, fore, flags);
    }
}
