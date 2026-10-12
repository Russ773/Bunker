using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Bunker;

/// <summary>Start-up splash: the logo, the app name, and the version/build bottom right.</summary>
public class SplashForm : Form
{
    private static readonly Image? Logo = LoadLogo();
    private float _progress;

    protected override bool ShowWithoutActivation => true;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        ClientSize = new Size(560, 340);
        BackColor = Theme.Bg;
        Opacity = 0;
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") == "1") { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); }
#endif
        if (Theme.AppIcon is { } icon) Icon = icon;
    }

    public static Image? LoadLogo()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
            return s is null ? null : new Bitmap(s);
        }
        catch { return null; }
    }

    /// <summary>Shows the splash for at least the given time, fading in and out, while the caller keeps working.</summary>
    public void RunFor(int milliseconds, Action work)
    {
        Show();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        work();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            var t = clock.ElapsedMilliseconds;
            _progress = Math.Min(1f, t / (float)milliseconds);
            Opacity = t < 250 ? t / 250.0 : t > milliseconds - 250 ? Math.Max(0, (milliseconds - t) / 250.0) : 1;
            Invalidate();
            Application.DoEvents();
            Thread.Sleep(15);
        }
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(24, 27, 33), Theme.Bg, 90f))
            g.FillRectangle(bg, ClientRectangle);

        if (Logo is not null) g.DrawImage(Logo, new Rectangle(ClientSize.Width / 2 - 70, 40, 140, 140));

        TextRenderer.DrawText(g, "BUNKER", new Font("Segoe UI Black", 28f), new Rectangle(0, 188, ClientSize.Width, 52),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "RUST SERVER ADMIN", new Font("Segoe UI", 10f), new Rectangle(0, 238, ClientSize.Width, 24),
            Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, "Starting...", Theme.Small, new Rectangle(18, ClientSize.Height - 38, 200, 24),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Updater.VersionText, Theme.Small, new Rectangle(ClientSize.Width - 258, ClientSize.Height - 38, 240, 24),
            Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        using (var track = new SolidBrush(Theme.Border)) g.FillRectangle(track, 0, ClientSize.Height - 4, ClientSize.Width, 4);
        using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 0, ClientSize.Height - 4, ClientSize.Width * _progress, 4);
        using var border = new Pen(Theme.Border);
        g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}
