using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>A small line chart over time with a gradient fill, grid, and a hover read-out.</summary>
public class LineChart : Control
{
    private IReadOnlyList<(DateTime Time, double Value)> _points = Array.Empty<(DateTime, double)>();
    private DateTime _from;
    private DateTime _to;
    private int _hover = -1;

    public string Title { get; set; } = "";
    public string Unit { get; set; } = "";
    public Color LineColor { get; set; } = Theme.Accent;
    public int Decimals { get; set; }
    /// <summary>When set, the vertical scale always starts at this value (for example 0).</summary>
    public double? Floor { get; set; }

    public LineChart()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        SetStyle(ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(220, 150);
    }

    public void SetData(IReadOnlyList<(DateTime Time, double Value)> points, DateTime from, DateTime to)
    {
        _points = points;
        _from = from;
        _to = to <= from ? from.AddSeconds(1) : to;
        _hover = -1;
        Invalidate();
    }

    private string Format(double v) => v.ToString("N" + Decimals) + Unit;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var plot = PlotArea();
        if (_points.Count == 0 || e.X < plot.Left || e.X > plot.Right) { if (_hover != -1) { _hover = -1; Invalidate(); } return; }
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _points.Count; i++)
        {
            var d = Math.Abs(XOf(_points[i].Time, plot) - e.X);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        if (best != _hover) { _hover = best; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    private Rectangle PlotArea() => new(54, 40, Math.Max(10, Width - 54 - 14), Math.Max(10, Height - 40 - 26));

    private float XOf(DateTime t, Rectangle plot) =>
        plot.Left + (float)((t - _from).TotalSeconds / (_to - _from).TotalSeconds) * plot.Width;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        TextRenderer.DrawText(g, Title, Theme.Semibold, new Point(14, 10), Theme.Text);
        var plot = PlotArea();

        if (_points.Count < 2)
        {
            TextRenderer.DrawText(g, "Waiting for data...", Theme.Small, plot, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var values = _points.Select(p => p.Value).ToList();
        var min = Floor ?? values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 1e-9) { max = min + 1; if (Floor is null) min -= 1; }
        var pad = (max - min) * 0.08;
        var top = max + pad;
        var bottom = Floor is not null ? min : min - pad;

        float YOf(double v) => plot.Bottom - (float)((v - bottom) / (top - bottom)) * plot.Height;

        // Grid lines and the vertical scale
        using (var grid = new Pen(Color.FromArgb(60, Theme.Border)))
        {
            for (var i = 0; i <= 3; i++)
            {
                var value = bottom + (top - bottom) * i / 3;
                var y = YOf(value);
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, value.ToString("N" + (Decimals > 0 || top - bottom < 6 ? Math.Max(Decimals, 1) : 0)), Theme.Small,
                    new Rectangle(0, (int)y - 9, 50, 18), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        // Time labels along the bottom
        for (var i = 0; i <= 3; i++)
        {
            var t = _from.AddSeconds((_to - _from).TotalSeconds * i / 3);
            var x = plot.Left + plot.Width * i / 3;
            TextRenderer.DrawText(g, t.ToString("HH:mm"), Theme.Small, new Rectangle(x - 30, plot.Bottom + 4, 60, 18), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Area and line
        var pts = _points.Select(p => new PointF(XOf(p.Time, plot), YOf(p.Value))).ToArray();
        using (var path = new GraphicsPath())
        {
            path.AddLine(pts[0].X, plot.Bottom, pts[0].X, pts[0].Y);
            if (pts.Length > 1) path.AddLines(pts);
            path.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, plot.Bottom);
            path.CloseFigure();
            using var fill = new LinearGradientBrush(plot, Color.FromArgb(70, LineColor), Color.FromArgb(0, LineColor), 90f);
            g.FillPath(fill, path);
        }
        using (var pen = new Pen(LineColor, 2f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);

        // Current value (top right) and range summary
        var latest = _points[^1].Value;
        TextRenderer.DrawText(g, Format(latest), new Font("Segoe UI Semibold", 13f), new Rectangle(Width - 170, 6, 156, 28), LineColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        if (_hover >= 0)
        {
            var p = pts[_hover];
            using (var guide = new Pen(Color.FromArgb(120, Theme.Muted)) { DashStyle = DashStyle.Dash }) g.DrawLine(guide, p.X, plot.Top, p.X, plot.Bottom);
            using (var dot = new SolidBrush(LineColor)) g.FillEllipse(dot, p.X - 4, p.Y - 4, 8, 8);
            var text = $"{_points[_hover].Time:HH:mm:ss}   {Format(_points[_hover].Value)}";
            var size = TextRenderer.MeasureText(text, Theme.Small);
            var box = new Rectangle((int)Math.Min(Math.Max(plot.Left, p.X - size.Width / 2f - 8), plot.Right - size.Width - 16), plot.Top + 2, size.Width + 16, size.Height + 8);
            using (var bg = new SolidBrush(Theme.Input)) g.FillRectangle(bg, box);
            using (var edge = new Pen(LineColor)) g.DrawRectangle(edge, box);
            TextRenderer.DrawText(g, text, Theme.Small, box, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>Four live charts (FPS, players, entities, memory) with a choice of time window.</summary>
public class HealthView : Panel
{
    private readonly Core _core;
    private readonly LineChart _fps = new() { Title = "Server FPS", LineColor = Theme.Ok, Decimals = 1, Floor = 0 };
    private readonly LineChart _players = new() { Title = "Players online", LineColor = Theme.Chat, Floor = 0 };
    private readonly LineChart _entities = new() { Title = "Entities", LineColor = Theme.Warn };
    private readonly LineChart _memory = new() { Title = "Memory", Unit = " MB", LineColor = Theme.Accent };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small };
    private readonly List<(Button Button, TimeSpan Window)> _windows = new();
    private TimeSpan _window = TimeSpan.FromMinutes(15);

    public HealthView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(0, 4, 0, 0), BackColor = Theme.Panel, WrapContents = false };
        foreach (var (label, window) in new[] { ("5 min", TimeSpan.FromMinutes(5)), ("15 min", TimeSpan.FromMinutes(15)), ("1 hour", TimeSpan.FromHours(1)), ("2 hours", TimeSpan.FromHours(2)) })
        {
            var b = Theme.GhostButton(label);
            b.Height = 34;
            b.Width = 84;
            b.Margin = new Padding(0, 0, 8, 0);
            var w = window;
            b.Click += (_, _) => { _window = w; Refresh(); };
            _windows.Add((b, window));
            top.Controls.Add(b);
        }
        _summary.Margin = new Padding(14, 10, 0, 0);
        top.Controls.Add(_summary);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var charts = new[] { _fps, _players, _entities, _memory };
        for (var i = 0; i < 4; i++)
        {
            charts[i].Dock = DockStyle.Fill;
            charts[i].Margin = new Padding(i % 2 == 0 ? 0 : 6, i < 2 ? 0 : 6, i % 2 == 0 ? 6 : 0, 0);
            grid.Controls.Add(charts[i], i % 2, i / 2);
        }

        Controls.Add(grid);
        Controls.Add(top);
        _core.Monitor.Updated += Refresh;
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Monitor.Updated -= Refresh;
        base.Dispose(disposing);
    }

    private new void Refresh()
    {
        foreach (var (button, window) in _windows)
        {
            var selected = window == _window;
            var b = (ThemedButton)button;
            b.BackColor = selected ? Theme.Accent : Theme.Panel;
            b.ForeColor = selected ? Color.White : Theme.Text;
            b.HoverBack = selected ? Theme.AccentHover : Theme.Input;
            b.Invalidate();
        }

        var now = DateTime.Now;
        var from = now - _window;
        var samples = _core.Monitor.History.Where(s => s.Time >= from).ToList();

        void Set(LineChart chart, Func<HealthSample, double> pick) =>
            chart.SetData(samples.Select(s => (s.Time, pick(s))).ToList(), from, now);

        Set(_fps, s => s.Fps);
        Set(_players, s => s.Players);
        Set(_entities, s => s.Entities);
        Set(_memory, s => s.MemoryMb);

        if (samples.Count == 0) _summary.Text = _core.State == ConnState.Connected ? "Collecting data..." : "Connect to a server to see its health.";
        else
        {
            var restarts = 0;
            for (var i = 1; i < samples.Count; i++) if (samples[i].UptimeSeconds + 30 < samples[i - 1].UptimeSeconds) restarts++;
            _summary.Text = $"FPS  avg {samples.Average(s => s.Fps):0}  -  min {samples.Min(s => s.Fps):0}  -  peak players {samples.Max(s => s.Players)}" +
                            (restarts > 0 ? $"  -  {restarts} restart{(restarts == 1 ? "" : "s")} seen" : "");
        }
    }
}
