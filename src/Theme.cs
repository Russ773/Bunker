using System.Drawing;
using System.Runtime.InteropServices;

namespace Bunker;

/// <summary>Shared colours, fonts and control styling so every window looks the same.</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(17, 19, 23);
    public static readonly Color Panel = Color.FromArgb(26, 29, 35);
    public static readonly Color Input = Color.FromArgb(32, 36, 43);
    public static readonly Color Border = Color.FromArgb(48, 53, 63);
    public static readonly Color Text = Color.FromArgb(230, 232, 236);
    public static readonly Color Muted = Color.FromArgb(138, 145, 156);
    public static readonly Color Accent = Color.FromArgb(224, 89, 42);
    public static readonly Color AccentHover = Color.FromArgb(240, 110, 62);
    public static readonly Color Ok = Color.FromArgb(76, 195, 138);
    public static readonly Color Warn = Color.FromArgb(240, 180, 41);
    public static readonly Color Err = Color.FromArgb(239, 83, 80);
    public static readonly Color Chat = Color.FromArgb(124, 196, 255);

    public static readonly Font UI = new("Segoe UI", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 22f);
    public static readonly Font Brand = new("Segoe UI Black", 13f);
    public static readonly Font Mono = new("Consolas", 10.5f);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Applies the dark background, UI font and a dark title bar to a window.</summary>
    public static void Apply(Form f)
    {
        f.BackColor = Bg;
        f.ForeColor = Text;
        f.Font = UI;
        f.HandleCreated += (_, _) => { int on = 1; DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)); };
    }

    public static Button PrimaryButton(string text) => Styled(text, Accent, AccentHover, Color.White, 0);

    public static Button GhostButton(string text) => Styled(text, Panel, Input, Text, 1);

    private static Button Styled(string text, Color back, Color hover, Color fore, int border)
    {
        var b = new Button
        {
            Text = text, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand, Height = 36, AutoSize = false, Font = Semibold,
            UseVisualStyleBackColor = false,
        };
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
        Font = Theme.UI, Dock = DockStyle.Fill,
    };

    public override string Text { get => Box.Text; set => Box.Text = value; }

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
    }

    /// <summary>Adds a control (e.g. a Show/Hide toggle) on the right edge, inside the border.</summary>
    public void AddTrailing(Control c)
    {
        c.Dock = DockStyle.Right;
        Controls.Add(c);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Box.Focused ? Theme.Accent : Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
}
