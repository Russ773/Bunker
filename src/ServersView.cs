using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>Which saved servers the Multi Server page shows, and how it lays their cards out. Kept free of controls so it can be tested.</summary>
public static class ServerGrid
{
    /// <summary>The most servers the page shows at once.</summary>
    public const int MaxShown = 6;

    /// <summary>
    /// The servers to show, in the order they are saved. With six or fewer saved, all of them. With more, the ones chosen (up to six),
    /// or the first six if nothing has been chosen yet or the chosen ones no longer exist.
    /// </summary>
    public static List<string> Pick(IReadOnlyList<string> savedIds, IReadOnlyCollection<string> chosen)
    {
        if (savedIds.Count <= MaxShown) return savedIds.ToList();
        var picked = savedIds.Where(chosen.Contains).Take(MaxShown).ToList();
        return picked.Count > 0 ? picked : savedIds.Take(MaxShown).ToList();
    }

    /// <summary>Columns and rows for <paramref name="count"/> cards in a window <paramref name="width"/> pixels wide. Every card is the same size.</summary>
    public static (int Columns, int Rows) Choose(int count, int width)
    {
        if (count <= 1) return (1, 1);
        var wide = width >= 1100;
        var medium = width >= 800;
        return count switch
        {
            2 => medium ? (2, 1) : (1, 2),
            3 => wide ? (3, 1) : medium ? (2, 2) : (1, 3),
            4 => medium ? (2, 2) : (1, 4),
            _ => wide ? (3, 2) : medium ? (2, (count + 1) / 2) : (1, count),
        };
    }

    /// <summary>The size of each card so the grid fills the space, but never smaller than a card can be read at.</summary>
    public static Size CardSize(int columns, int rows, Size available, int gap, Size minimum) =>
        new(Math.Max(minimum.Width, (available.Width - (columns - 1) * gap) / columns),
            Math.Max(minimum.Height, (available.Height - (rows - 1) * gap) / rows));
}

/// <summary>One saved server on the Multi Server page: its status, player count, a chart of recent players, who is on, and buttons to open it.</summary>
public sealed class ServerCard : Panel
{
    public static readonly Size Minimum = new(340, 290); // the shortest a card can be with every line (and the buttons) clear of each other
    private const int Pad = 18;
    private const int ButtonsHeight = 32;

    private readonly Label _name = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _address = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Label _status = new() { Font = Theme.Small, AutoSize = false, TextAlign = ContentAlignment.TopRight };
    private readonly Label _players = new() { Font = new Font("Segoe UI Semibold", 20f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _sub = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false };
    private readonly Label _stats = new() { Font = Theme.Small, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true };
    private readonly Label _map = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Label _who = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true };
    private readonly Button _open = Theme.PrimaryButton("Open");
    private readonly Button _console = Theme.GhostButton("Console");
    private WatchState _state = WatchState.Idle;
    private int _max, _count;
    private IReadOnlyList<int> _history = Array.Empty<int>();
    private IReadOnlyList<PlayerInfo> _names = Array.Empty<PlayerInfo>();
    private Rectangle _chart, _bar, _list;

    public string ServerId { get; }
    public event Action<string, string>? OpenRequested;

    public ServerCard(ServerProfile profile)
    {
        ServerId = profile.Id;
        Size = Minimum;
        BackColor = Theme.Panel;
        DoubleBuffered = true;
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
            _names = w!.Players;
            _who.Text = _names.Count == 0 ? "Nobody online" : WatchText.PlayerLine(_names.Select(p => p.Name).ToList());
            _history = w.PlayerHistory.ToList();
        }
        else
        {
            _count = 0;
            _players.Text = "-";
            _sub.Text = _state == WatchState.Offline ? w?.Detail ?? "" : "";
            _stats.Text = _map.Text = _who.Text = "";
            _names = Array.Empty<PlayerInfo>();
            _history = Array.Empty<int>();
        }
        LayoutParts();
        Tips.Set(_sub, _sub.Text);
        Tips.Set(_who, _who.Text);
        Invalidate();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        LayoutParts();
        Invalidate();
    }

    /// <summary>
    /// Places everything from the card's size, so a bigger card uses its room: a taller chart, more player names, buttons kept at the foot.
    /// </summary>
    private void LayoutParts()
    {
        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0) return;
        var online = _state == WatchState.Online;

        _name.SetBounds(36, 14, Math.Max(60, w - 190), 22);
        _address.SetBounds(36, 38, Math.Max(60, w - 190), 18);
        _status.SetBounds(w - 150, 16, 134, 20);

        var top = 74;
        var chartHeight = Math.Clamp(54 + (h - Minimum.Height) / 3, 54, 140);
        _players.SetBounds(Pad, top + 2, Math.Max(100, w / 2 - Pad), 36);
        _sub.SetBounds(Pad + 2, top + 40, online ? Math.Max(120, w / 2 - Pad) : w - 2 * Pad - 4, 34);
        _chart = new Rectangle(w / 2, top, w - w / 2 - Pad, chartHeight);

        var barY = top + Math.Max(78, chartHeight + 8);
        _bar = new Rectangle(Pad, barY, w - 2 * Pad, 6);
        _stats.SetBounds(Pad, barY + 14, w - 2 * Pad, 18);
        _map.SetBounds(Pad, barY + 34, w - 2 * Pad, 18);

        var buttonsY = h - ButtonsHeight - 14;
        _open.SetBounds(Pad, buttonsY, 100, ButtonsHeight);
        _console.SetBounds(Pad + 108, buttonsY, 100, ButtonsHeight);

        // Whatever room is left between the stats and the buttons lists who is online. A small card gets one line instead.
        var whoY = barY + 58;
        var room = buttonsY - 8 - whoY;
        if (online && _names.Count > 0 && room >= 58)
        {
            _list = new Rectangle(Pad, whoY, w - 2 * Pad, room);
            _who.Visible = false;
        }
        else
        {
            _list = Rectangle.Empty;
            _who.SetBounds(Pad, whoY, w - 2 * Pad, 18);
            _who.Visible = true;
        }
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
        using (var back = new SolidBrush(Theme.Input)) g.FillRectangle(back, _bar);
        if (_state == WatchState.Online && _max > 0)
        {
            var fraction = Math.Clamp(_count / (double)_max, 0, 1);
            using var fill = new SolidBrush(fraction >= 0.9 ? Theme.Warn : Theme.Accent);
            g.FillRectangle(fill, _bar.X, _bar.Y, (int)Math.Round(_bar.Width * fraction), _bar.Height);
        }

        // The last few minutes of player counts.
        if (_history.Count >= 2)
        {
            var peak = _history.Max();
            var top = Math.Max(peak * 1.25, peak + 2); // a little headroom, so a steady count is a line across the chart, not stuck to its top edge
            var points = _history.Select((v, i) => new PointF(
                _chart.X + _chart.Width * i / (float)(_history.Count - 1),
                _chart.Bottom - _chart.Height * v / (float)top)).ToArray();
            using var pen = new Pen(Theme.Chat, 1.6f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, points);
        }
        else if (_state == WatchState.Online)
        {
            using var f = new Font(Theme.Small, FontStyle.Italic);
            TextRenderer.DrawText(g, "chart fills in as it watches", f, _chart, Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        if (!_list.IsEmpty) DrawNames(g);
    }

    /// <summary>Who is online, in as many columns and rows as the card has room for, with a "+N more" if they do not all fit.</summary>
    private void DrawNames(Graphics g)
    {
        TextRenderer.DrawText(g, "ONLINE NOW", new Font("Segoe UI Semibold", 7.5f), new Rectangle(_list.X, _list.Y, _list.Width, 14), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        const int lineHeight = 18;
        const int columnWidth = 170;
        var columns = Math.Max(1, _list.Width / columnWidth);
        var cellWidth = Math.Min(_list.Width / columns, 240); // names sit close to their ping, however wide the card
        var rows = Math.Max(1, (_list.Height - 18) / lineHeight);
        var capacity = rows * columns;
        var names = _names.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var shown = names.Count <= capacity ? names.Count : capacity - 1;
        for (var i = 0; i < shown; i++) DrawName(g, i, cellWidth, rows, lineHeight, names[i].Name, $"{names[i].Ping}ms");
        if (shown < names.Count) DrawName(g, shown, cellWidth, rows, lineHeight, $"+{names.Count - shown} more", "");
    }

    private void DrawName(Graphics g, int index, int cellWidth, int rows, int lineHeight, string text, string ping)
    {
        var column = index / rows;
        var row = index % rows;
        var cell = new Rectangle(_list.X + column * cellWidth, _list.Y + 18 + row * lineHeight, cellWidth - 14, lineHeight);
        TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(cell.X, cell.Y, cell.Width - 44, cell.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (ping.Length > 0)
            TextRenderer.DrawText(g, ping, Theme.Small, new Rectangle(cell.Right - 44, cell.Y, 44, cell.Height), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);
    }
}

/// <summary>Pick which saved servers the Multi Server page shows, when there are more than it has room for.</summary>
public sealed class ChooseServersForm : Form
{
    private readonly List<(CheckBox Box, string Id)> _boxes = new();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small };

    public List<string> Chosen => _boxes.Where(b => b.Box.Checked).Select(b => b.Id).ToList();

    public ChooseServersForm(IReadOnlyList<ServerProfile> servers, IReadOnlyCollection<string> shownNow)
    {
        Theme.Apply(this);
        Text = "Choose servers";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, Math.Min(560, 190 + servers.Count * 32));

        var ok = Theme.PrimaryButton("Save");
        var cancel = Theme.GhostButton("Cancel");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(28, 20, 28, 8) };
        flow.Controls.Add(new Label { Text = $"The Multi Server page shows up to {ServerGrid.MaxShown} servers at a time. Tick the ones to show.", ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 0, 0, 12) });
        foreach (var s in servers)
        {
            var box = new CheckBox { Text = s.DisplayName, AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Checked = shownNow.Contains(s.Id), Margin = new Padding(0, 4, 0, 4) };
            box.CheckedChanged += (_, _) => Update(box);
            _boxes.Add((box, s.Id));
            flow.Controls.Add(box);
        }
        flow.Controls.Add(_count);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Bg, Padding = new Padding(28, 10, 28, 10) };
        cancel.Dock = DockStyle.Right;
        ok.Dock = DockStyle.Right;
        ok.Margin = new Padding(0);
        bar.Controls.Add(cancel);
        bar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        bar.Controls.Add(ok);

        Controls.Add(flow);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Update(null);
    }

    // At the limit, the boxes that are not ticked are switched off, so there is no way to pick a seventh.
    private void Update(CheckBox? changed)
    {
        var ticked = _boxes.Count(b => b.Box.Checked);
        foreach (var (box, _) in _boxes) box.Enabled = box.Checked || ticked < ServerGrid.MaxShown;
        _count.Text = $"{ticked} of {ServerGrid.MaxShown} chosen";
        _count.ForeColor = ticked == 0 ? Theme.Warn : Theme.Muted;
    }
}

/// <summary>
/// Your servers at a glance, up to six side by side and all the same size. While this page is open, Bunker keeps a light connection to each
/// one to show who is online, and it can send a message or a command to all the online ones at once. The connections close when you leave.
/// </summary>
public sealed class ServersView : Panel
{
    private const int Gap = 14;
    private readonly Core _core;
    private readonly Label _summary = new() { Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true };
    private readonly Label _limit = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true };
    private readonly Button _choose = Theme.GhostButton("Choose servers...");
    private readonly Button _add = Theme.GhostButton("Add a server...");
    private readonly Panel _grid = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
    private readonly ModernField _text = new();
    private readonly Button _say = Theme.PrimaryButton("Say to all");
    private readonly Button _run = Theme.GhostButton("Run on all");
    private readonly Label _result = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false };
    private readonly Label _empty = new() { Font = Theme.UI, ForeColor = Theme.Muted, AutoSize = true, Text = "You have no servers saved yet. Add one, then come back here to see them all together." };
    private readonly List<ServerWatcher> _watchers = new();
    private readonly Dictionary<string, ServerCard> _cards = new();
    private int _refreshQueued;

    /// <summary>The server to switch to, and the page to show: "dashboard" or "console".</summary>
    public event Action<string, string>? OpenRequested;

    public ServersView(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        DarkScroll.Apply(_grid);

        // The strip along the top: how many are online, and a way to add or choose servers.
        var top = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Bg };
        _summary.Location = new Point(2, 6);
        _limit.Location = new Point(2, 28);
        _add.Height = _choose.Height = 34;
        _add.Margin = _choose.Margin = new Padding(0);
        _add.Dock = DockStyle.Right;
        _choose.Dock = DockStyle.Right;
        top.Controls.Add(_summary);
        top.Controls.Add(_limit);
        top.Controls.Add(_add);
        top.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        top.Controls.Add(_choose);

        // The "send to all" strip along the bottom.
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Panel, Padding = new Padding(16, 12, 16, 8) };
        var heading = new Label { Text = "Send to all online servers", Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(16, 12) };
        _text.SetBounds(16, 38, 560, 34);
        _text.Box.PlaceholderText = "A message for every player, or a console command";
        _say.Location = new Point(590, 36);
        _run.Location = new Point(_say.Right + 8, 36);
        _result.SetBounds(16, 78, 900, 20);
        Tips.Set(_say, "Posts the message in the chat of every online server (with the say command).");
        Tips.Set(_run, "Runs what you typed as a console command on every online server. You are asked to confirm first.");
        Tips.Set(_choose, $"Pick which servers the page shows (up to {ServerGrid.MaxShown})");
        Tips.Set(_add, "Save another server. It appears here straight away");
        bottom.Controls.AddRange(new Control[] { heading, _text, _say, _run, _result });

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(top);

        _say.Click += async (_, _) => await SendAllAsync(say: true);
        _run.Click += async (_, _) => await SendAllAsync(say: false);
        _add.Click += (_, _) => AddServer();
        _choose.Click += (_, _) => ChooseServers();
        _grid.Resize += (_, _) => LayoutGrid();
        _core.ServerChanged += OnServersChanged;
        VisibleChanged += (_, _) => { if (Visible) StartWatching(); else StopWatching(); };
        Disposed += (_, _) => { _core.ServerChanged -= OnServersChanged; StopWatching(); };
        Rebuild();
    }

    private List<ServerProfile> Shown()
    {
        var saved = _core.Settings.Servers;
        var ids = ServerGrid.Pick(saved.Select(s => s.Id).ToList(), _core.Settings.OverviewServerIds);
        return saved.Where(s => ids.Contains(s.Id)).ToList();
    }

    private void OnServersChanged()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() => { StopWatching(); Rebuild(); if (Visible) StartWatching(); });
    }

    private void AddServer()
    {
        using var f = new OnboardingForm(null, firstRun: false);
        if (f.ShowDialog(FindForm()) == DialogResult.OK) _core.SaveServer(f.Result!, makeActive: _core.Settings.ActiveServer is null);
    }

    private void ChooseServers()
    {
        using var f = new ChooseServersForm(_core.Settings.Servers, Shown().Select(s => s.Id).ToList());
        if (f.ShowDialog(FindForm()) != DialogResult.OK || f.Chosen.Count == 0) return;
        _core.SaveOverviewServers(f.Chosen);
    }

    private void Rebuild()
    {
        _grid.SuspendLayout();
        foreach (var card in _cards.Values) { _grid.Controls.Remove(card); card.Dispose(); }
        _cards.Clear();
        _grid.Controls.Remove(_empty);
        foreach (var profile in Shown())
        {
            var card = new ServerCard(profile);
            card.OpenRequested += (id, page) => OpenRequested?.Invoke(id, page);
            _cards[profile.Id] = card;
            _grid.Controls.Add(card);
        }
        if (_cards.Count == 0) _grid.Controls.Add(_empty);

        var saved = _core.Settings.Servers.Count;
        _choose.Visible = saved > ServerGrid.MaxShown;
        _add.Visible = true;
        _limit.Text = saved > ServerGrid.MaxShown ? $"Showing {_cards.Count} of {saved} saved servers (the page shows up to {ServerGrid.MaxShown})"
            : saved == ServerGrid.MaxShown ? $"All {ServerGrid.MaxShown} places are in use"
            : $"You can show up to {ServerGrid.MaxShown} servers here";
        _grid.ResumeLayout();
        LayoutGrid();
        RefreshAll();
    }

    /// <summary>Fills the page: cards of one size in as many columns and rows as suit the window. A window too small for that scrolls.</summary>
    private void LayoutGrid()
    {
        if (_cards.Count == 0) { _empty.Location = new Point(4, 8); return; }
        var available = _grid.ClientSize;
        if (available.Width <= 0 || available.Height <= 0) return;

        var (columns, rows) = ServerGrid.Choose(_cards.Count, available.Width);
        // Columns too narrow to read give way to fewer columns.
        while (columns > 1 && (available.Width - (columns - 1) * Gap) / columns < ServerCard.Minimum.Width)
        {
            columns--;
            rows = (_cards.Count + columns - 1) / columns;
        }
        var size = ServerGrid.CardSize(columns, rows, available, Gap, ServerCard.Minimum);

        _grid.SuspendLayout();
        var i = 0;
        foreach (var card in _cards.Values)
        {
            card.Size = size;
            card.Location = new Point((i % columns) * (size.Width + Gap), (i / columns) * (size.Height + Gap));
            i++;
        }
        var totalHeight = rows * size.Height + (rows - 1) * Gap;
        _grid.AutoScrollMinSize = totalHeight > available.Height ? new Size(0, totalHeight) : Size.Empty;
        _grid.ResumeLayout(true);
    }

    private void StartWatching()
    {
        if (_watchers.Count > 0) return;
        foreach (var profile in Shown())
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
