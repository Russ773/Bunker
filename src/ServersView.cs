using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>One saved server on the overview: its status, player count, a small chart of recent players, and buttons to open it.</summary>
public sealed class ServerCard : Panel
{
    private readonly Label _name = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _address = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Label _status = new() { Font = Theme.Small, AutoSize = false, TextAlign = ContentAlignment.TopRight };
    private readonly Label _players = new() { Font = new Font("Segoe UI Semibold", 18f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _sub = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false };
    private readonly Label _stats = new() { Font = Theme.Small, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _map = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Label _who = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Button _open = Theme.PrimaryButton("Open");
    private readonly Button _console = Theme.GhostButton("Console");
    private WatchState _state = WatchState.Idle;
    private int _max, _count;
    private IReadOnlyList<int> _history = Array.Empty<int>();

    public string ServerId { get; }
    public event Action<string, string>? OpenRequested;

    public ServerCard(ServerProfile profile)
    {
        ServerId = profile.Id;
        Size = new Size(380, 262);
        Margin = new Padding(0, 0, 14, 14);
        BackColor = Theme.Panel;
        DoubleBuffered = true;

        _name.SetBounds(36, 14, 220, 22);
        _address.SetBounds(36, 38, 220, 18);
        _status.SetBounds(Width - 150, 16, 134, 20);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _players.SetBounds(18, 78, 180, 34);
        _sub.SetBounds(20, 114, 200, 32);
        _stats.SetBounds(18, 154, Width - 36, 18);
        _map.SetBounds(18, 174, Width - 36, 18);
        _who.SetBounds(18, 194, Width - 36, 18);
        _open.SetBounds(18, 218, 100, 32);
        _console.SetBounds(126, 218, 100, 32);
        Controls.AddRange(new Control[] { _name, _address, _status, _players, _sub, _stats, _map, _who, _open, _console });

        _name.Text = profile.DisplayName;
        _address.Text = $"{profile.Host}:{profile.Port}";
        Tips.Set(_name, profile.DisplayName);
        _open.Click += (_, _) => OpenRequested?.Invoke(ServerId, "dashboard");
        _console.Click += (_, _) => OpenRequested?.Invoke(ServerId, "console");
        Apply(null);
    }

    /// <summary>Shows what the watcher currently knows.</summary>
    public void Apply(ServerWatcher? w)
    {
        _state = w?.State ?? WatchState.Idle;
        var info = w?.Info;
        switch (_state)
        {
            case WatchState.Online: _status.Text = "Online"; _status.ForeColor = Theme.Ok; break;
            case WatchState.Connecting: _status.Text = "Connecting..."; _status.ForeColor = Theme.Warn; break;
            case WatchState.Offline: _status.Text = "Offline"; _status.ForeColor = Theme.Err; break;
            default: _status.Text = "Waiting"; _status.ForeColor = Theme.Muted; break;
        }

        if (_state == WatchState.Online && info is not null)
        {
            _count = info.Players;
            _max = Math.Max(info.MaxPlayers, 1);
            _players.Text = $"{info.Players} / {info.MaxPlayers}";
            _sub.Text = $"{info.Queued} queued, {info.Joining} joining";
            var up = TimeSpan.FromSeconds(info.UptimeSeconds);
            var uptime = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m";
            _stats.Text = $"FPS {info.Fps:0}   ·   Up {uptime}   ·   {info.Entities:N0} entities";
            _map.Text = string.IsNullOrEmpty(info.Map) ? "" : "Map: " + info.Map;
            _who.Text = w!.Players.Count == 0 ? "Nobody online" : WatchText.PlayerLine(w.Players.Select(p => p.Name).ToList());
            _history = w.PlayerHistory.ToList();
        }
        else
        {
            _count = 0;
            _players.Text = "-";
            _sub.Text = _state == WatchState.Offline ? w?.Detail ?? "" : "";
            _sub.Size = new Size(Width - 40, 34);
            _stats.Text = _map.Text = _who.Text = "";
            _history = Array.Empty<int>();
        }
        if (_state == WatchState.Online) _sub.Size = new Size(200, 32);
        Tips.Set(_sub, _sub.Text);
        Tips.Set(_who, _who.Text);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var dot = _state switch { WatchState.Online => Theme.Ok, WatchState.Connecting => Theme.Warn, WatchState.Offline => Theme.Err, _ => Theme.Dim };
        using (var br = new SolidBrush(dot)) g.FillEllipse(br, 18, 20, 10, 10);

        // How full the server is.
        var bar = new Rectangle(18, 142, Width - 36, 6);
        using (var back = new SolidBrush(Theme.Input)) g.FillRectangle(back, bar);
        if (_state == WatchState.Online && _max > 0)
        {
            var fraction = Math.Clamp(_count / (double)_max, 0, 1);
            using var fill = new SolidBrush(fraction >= 0.9 ? Theme.Warn : Theme.Accent);
            g.FillRectangle(fill, bar.X, bar.Y, (int)Math.Round(bar.Width * fraction), bar.Height);
        }

        // The last few minutes of player counts.
        var chart = new Rectangle(Width - 178, 72, 160, 54);
        if (_history.Count >= 2)
        {
            var peak = _history.Max();
            var top = Math.Max(peak * 1.25, peak + 2); // a little headroom, so a steady count is a line across the chart, not stuck to its top edge
            var points = _history.Select((v, i) => new PointF(
                chart.X + chart.Width * i / (float)(_history.Count - 1),
                chart.Bottom - chart.Height * v / (float)top)).ToArray();
            using var pen = new Pen(Theme.Chat, 1.6f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, points);
        }
        else if (_state == WatchState.Online)
        {
            using var f = new Font(Theme.Small, FontStyle.Italic);
            TextRenderer.DrawText(g, "chart fills in as it watches", f, chart, Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }
}

/// <summary>
/// Every saved server at a glance. While this page is open, Bunker keeps a light connection to each saved server to show who is online,
/// and it can send a message or a command to all the online ones at once. The connections close when you leave the page.
/// </summary>
public sealed class ServersView : Panel
{
    private readonly Core _core;
    private readonly Label _summary = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true };
    private readonly FlowLayoutPanel _flow = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = Theme.Bg, Padding = new Padding(2, 4, 0, 0) };
    private readonly ModernField _text = new();
    private readonly Button _say = Theme.PrimaryButton("Say to all");
    private readonly Button _run = Theme.GhostButton("Run on all");
    private readonly Label _result = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Dock = DockStyle.Bottom, Height = 20 };
    private readonly Label _empty = new() { Font = Theme.UI, ForeColor = Theme.Muted, AutoSize = true, Text = "You have no servers saved yet. Add one in Settings, then come back here to see them all together." };
    private readonly List<ServerWatcher> _watchers = new();
    private readonly Dictionary<string, ServerCard> _cards = new();
    private int _refreshQueued;

    /// <summary>The server to switch to, and the page to show: "dashboard" or "console".</summary>
    public event Action<string, string>? OpenRequested;

    public ServersView(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        DarkScroll.Apply(_flow);

        var top = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Bg };
        _summary.Location = new Point(2, 4);
        top.Controls.Add(_summary);

        // The "send to all" strip along the bottom.
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Panel, Padding = new Padding(16, 12, 16, 8) };
        var heading = new Label { Text = "Send to all online servers", Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(16, 12) };
        _text.SetBounds(16, 38, 560, 34);
        _text.Box.PlaceholderText = "A message for every player, or a console command";
        _say.Location = new Point(590, 36);
        _run.Location = new Point(_say.Right + 8, 36);
        _result.Dock = DockStyle.None;
        _result.SetBounds(16, 78, 900, 20);
        Tips.Set(_say, "Posts the message in the chat of every online server (with the say command).");
        Tips.Set(_run, "Runs what you typed as a console command on every online server. You are asked to confirm first.");
        bottom.Controls.AddRange(new Control[] { heading, _text, _say, _run, _result });

        _empty.Location = new Point(4, 8);
        Controls.Add(_flow);
        Controls.Add(bottom);
        Controls.Add(top);

        _say.Click += async (_, _) => await SendAllAsync(say: true);
        _run.Click += async (_, _) => await SendAllAsync(say: false);
        _core.ServerChanged += OnServersChanged;
        VisibleChanged += (_, _) => { if (Visible) StartWatching(); else StopWatching(); };
        Disposed += (_, _) => { _core.ServerChanged -= OnServersChanged; StopWatching(); };
        Rebuild();
    }

    private void OnServersChanged()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() => { StopWatching(); Rebuild(); if (Visible) StartWatching(); });
    }

    private void Rebuild()
    {
        _flow.SuspendLayout();
        foreach (var card in _cards.Values) { _flow.Controls.Remove(card); card.Dispose(); }
        _cards.Clear();
        _flow.Controls.Remove(_empty);
        foreach (var profile in _core.Settings.Servers)
        {
            var card = new ServerCard(profile);
            card.OpenRequested += (id, page) => OpenRequested?.Invoke(id, page);
            _cards[profile.Id] = card;
            _flow.Controls.Add(card);
        }
        if (_cards.Count == 0) _flow.Controls.Add(_empty);
        _flow.ResumeLayout();
        RefreshAll();
    }

    private void StartWatching()
    {
        if (_watchers.Count > 0) return;
        foreach (var profile in _core.Settings.Servers)
        {
            var watcher = new ServerWatcher(profile);
            watcher.Changed += QueueRefresh;
            _watchers.Add(watcher);
            watcher.Start();
        }
        RefreshAll();
    }

    private void StopWatching()
    {
        foreach (var w in _watchers) { w.Changed -= QueueRefresh; w.Dispose(); }
        _watchers.Clear();
        if (!IsDisposed) RefreshAll();
    }

    /// <summary>Watchers report from background threads, and a busy moment can bring several at once: do one redraw on the UI thread.</summary>
    private void QueueRefresh()
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        try { BeginInvoke(() => { Interlocked.Exchange(ref _refreshQueued, 0); RefreshAll(); }); }
        catch (InvalidOperationException) { }
    }

    private void RefreshAll()
    {
        foreach (var (id, card) in _cards) card.Apply(_watchers.FirstOrDefault(w => w.Profile.Id == id));
        var online = _watchers.Where(w => w.State == WatchState.Online).ToList();
        _summary.Text = WatchText.Summary(_cards.Count, online.Count, online.Sum(w => w.Info?.Players ?? 0));
        _say.Enabled = _run.Enabled = online.Count > 0;
    }

    private async Task SendAllAsync(bool say)
    {
        var text = _text.Text.Trim();
        var targets = _watchers.Where(w => w.State == WatchState.Online).ToList();
        if (text.Length == 0 || targets.Count == 0) return;

        var command = say ? ChatSender.SayCommand(_core.Settings.ChatName, text) : text;
        var names = WatchText.NameList(targets.Select(t => t.Profile.DisplayName).ToList());
        var question = say
            ? $"Say this in the chat of {names}?\n\n{text}"
            : $"Run this command on {names}?\n\n{command}";
        if (MessageBox.Show(FindForm(), question, say ? "Say to all servers" : "Run on all servers", MessageBoxButtons.YesNo,
                say ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _say.Enabled = _run.Enabled = false;
        _result.ForeColor = Theme.Muted;
        _result.Text = "Sending...";
        var results = await Task.WhenAll(targets.Select(async t => (Name: t.Profile.DisplayName, Reply: await t.SendAsync(command))));
        var failed = results.Count(r => r.Reply is null);
        _result.Text = string.Join("      ", results.Select(r => r.Reply is null ? $"{r.Name}: not sent" : $"{r.Name}: sent"));
        _result.ForeColor = failed == 0 ? Theme.Ok : Theme.Warn;
        if (failed == 0) _text.Text = "";
        RefreshAll();
    }
}
