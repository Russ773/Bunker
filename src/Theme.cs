using System.Drawing;
using System.Runtime.InteropServices;

namespace Bunker;

/// <summary>Shared colours, fonts and control styling so every window looks the same.</summary>
public static class Theme
{
    /// <summary>True when the light theme is active. Chosen once at start-up by Init, before any window is created.</summary>
    public static bool IsLight { get; private set; }

    public static Color Bg { get; private set; } = Color.FromArgb(17, 19, 23);
    public static Color Panel { get; private set; } = Color.FromArgb(26, 29, 35);
    public static Color Input { get; private set; } = Color.FromArgb(32, 36, 43);
    public static Color Border { get; private set; } = Color.FromArgb(48, 53, 63);
    public static Color Text { get; private set; } = Color.FromArgb(230, 232, 236);
    public static Color Muted { get; private set; } = Color.FromArgb(138, 145, 156);
    public static Color Accent { get; private set; } = Color.FromArgb(224, 89, 42);
    public static Color AccentHover { get; private set; } = Color.FromArgb(240, 110, 62);
    public static Color Ok { get; private set; } = Color.FromArgb(76, 195, 138);
    public static Color Warn { get; private set; } = Color.FromArgb(240, 180, 41);
    public static Color Err { get; private set; } = Color.FromArgb(239, 83, 80);
    public static Color Chat { get; private set; } = Color.FromArgb(124, 196, 255);

    // Less common shades that also have to change with the theme
    public static Color Hover { get; private set; } = Color.FromArgb(30, 34, 41);
    public static Color Selection { get; private set; } = Color.FromArgb(52, 38, 32);
    public static Color GridLine { get; private set; } = Color.FromArgb(36, 40, 48);
    public static Color Dim { get; private set; } = Color.FromArgb(90, 96, 108);
    public static Color SplashTop { get; private set; } = Color.FromArgb(24, 27, 33);
    public static Color DisabledBack { get; private set; } = Color.FromArgb(52, 57, 66);
    public static Color DisabledFore { get; private set; } = Color.FromArgb(132, 139, 150);

    /// <summary>Picks the palette. Must run before any window or control is created.</summary>
    public static void Init(bool light)
    {
        IsLight = light;
        if (!light) return; // the dark palette is the default above

        Bg = Color.FromArgb(243, 244, 247);
        Panel = Color.FromArgb(255, 255, 255);
        Input = Color.FromArgb(240, 242, 246);
        Border = Color.FromArgb(214, 219, 227);
        Text = Color.FromArgb(28, 31, 38);
        Muted = Color.FromArgb(104, 112, 126);
        Accent = Color.FromArgb(220, 82, 34);
        AccentHover = Color.FromArgb(196, 66, 22);
        Ok = Color.FromArgb(22, 140, 88);
        Warn = Color.FromArgb(176, 118, 0);
        Err = Color.FromArgb(206, 48, 44);
        Chat = Color.FromArgb(18, 106, 196);
        Hover = Color.FromArgb(236, 239, 244);
        Selection = Color.FromArgb(253, 233, 223);
        GridLine = Color.FromArgb(228, 231, 237);
        Dim = Color.FromArgb(150, 157, 168);
        SplashTop = Color.FromArgb(255, 255, 255);
        DisabledBack = Color.FromArgb(222, 226, 232);
        DisabledFore = Color.FromArgb(142, 149, 160);
    }

    public static readonly Font UI = new("Segoe UI", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 22f);
    public static readonly Font Brand = new("Segoe UI Black", 13f);
    public static readonly Font Mono = new("Consolas", 10.5f);

    /// <summary>The icon embedded in the exe, so every window shows the Bunker logo.</summary>
    public static readonly Icon? AppIcon = LoadIcon();

    private static Icon? LoadIcon()
    {
        try { return Environment.ProcessPath is { } p ? Icon.ExtractAssociatedIcon(p) : null; }
        catch { return null; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Applies the dark background, UI font and a dark title bar to a window.</summary>
    public static void Apply(Form f)
    {
        f.BackColor = Bg;
        f.ForeColor = Text;
        f.Font = UI;
        if (AppIcon is not null) f.Icon = AppIcon;
        f.HandleCreated += (_, _) => { int on = IsLight ? 0 : 1; DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)); };
    }

    public static Button PrimaryButton(string text) => Styled(text, Accent, AccentHover, Color.White, 0);

    public static Button GhostButton(string text) => Styled(text, Panel, Input, Text, 1);

    private static Button Styled(string text, Color back, Color hover, Color fore, int border)
    {
        var b = new ThemedButton
        {
            Text = text, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand, Height = 36, AutoSize = false, Font = Semibold,
            UseVisualStyleBackColor = false,
        };
        b.BorderSize = border;
        b.BorderColor = Border;
        b.HoverBack = hover;
        b.FlatAppearance.BorderSize = border;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = hover;
        b.Width = Math.Max(90, TextRenderer.MeasureText(text, Semibold).Width + 36);
        return b;
    }
}


/// <summary>A flat dark text field with a border that lights up with the accent colour on focus.</summary>
public class ModernField : Panel
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text,
        Font = Theme.UI, AutoSize = false,
    };

    public override string? Text { get => Box.Text; set => Box.Text = value ?? ""; }

    public ModernField()
    {
        DoubleBuffered = true;
        BackColor = Theme.Input;
        Padding = new Padding(12, 9, 12, 9);
        Height = Box.PreferredHeight + 20;
        Controls.Add(Box);
        Box.Enter += (_, _) => Invalidate();
        Box.Leave += (_, _) => Invalidate();
        Box.EnabledChanged += (_, _) => Invalidate();
        Box.FontChanged += (_, _) => PerformLayout();
    }

    /// <summary>Adds a control (e.g. a Show/Hide toggle) on the right edge, inside the border.</summary>
    public void AddTrailing(Control c)
    {
        c.Dock = DockStyle.Right;
        Controls.Add(c);
    }

    /// <summary>The text box is sized from its font (with room for descenders) and centred, so large fonts are never clipped.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var right = Padding.Right;
        foreach (Control c in Controls) if (c != Box && c.Dock == DockStyle.Right) right += c.Width;
        var h = Box.PreferredHeight + 6;
        Box.SetBounds(Padding.Left, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - Padding.Left - right), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Box.Focused ? Theme.Accent : Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
}
