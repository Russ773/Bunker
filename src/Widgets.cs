using System.Drawing;

namespace Bunker;

/// <summary>Console view (all output + command box) or Chat view (chat only, input sends "say").</summary>
public class ConsoleView : Panel
{
    private readonly Core _core;
    private readonly bool _chat;
    private readonly bool _showInput;
    private readonly RichTextBox _box = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg,
        ForeColor = Theme.Text, Font = Theme.Mono, HideSelection = false, DetectUrls = false,
    };
    private readonly ModernField _input = new();
    private readonly Button _send = Theme.PrimaryButton("Send");
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 12, 0) };
    private readonly Label _clear = new() { Text = "Clear", AutoSize = true, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 4, 0) };
    private readonly List<string> _history = new();
    private Panel? _bar;
    private int _historyIndex;

    /// <summary>Controls the hosting Card should show on the right of its header.</summary>
    public Control[] HeaderActions => new Control[] { _autoScroll, _clear };

    public ConsoleView(Core core, bool chat = false, bool showInput = true)
    {
        _core = core;
        _chat = chat;
        _showInput = showInput;
        BackColor = Theme.Panel;

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(10, 8, 6, 6) };
        host.Controls.Add(_box);
        Controls.Add(host);

        if (showInput)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Panel };
            _input.Dock = DockStyle.Fill;
            _input.Box.Font = Theme.Mono;
            _input.Box.PlaceholderText = chat ? "Say something to the server..." : "Type a command (e.g. status, say hello)";
            var gap = new Panel { Dock = DockStyle.Right, Width = 8 };
            _send.Dock = DockStyle.Right;
            _send.Width = 86;
            _send.Height = 36;
            bar.Controls.AddRange(new Control[] { _input, gap, _send });
            Controls.Add(bar);
            _bar = bar;
            _send.Click += async (_, _) => await SendAsync();
            _input.Box.KeyDown += InputKeyDown;
            ApplyEnabled();
        }

        DarkScroll.Apply(_box);
        ApplyFont();
        _core.ConsoleFontChanged += ApplyFont;
        foreach (var e in _core.Log.Entries) Append(e);
        _core.Log.Added += OnAdded;
        _core.Log.Cleared += OnCleared;
        _core.StateChanged += ApplyEnabled;
        _clear.Click += (_, _) => _core.Log.Clear();
        _clear.MouseEnter += (_, _) => _clear.ForeColor = Theme.Accent;
        _clear.MouseLeave += (_, _) => _clear.ForeColor = Theme.Muted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Log.Added -= OnAdded;
            _core.Log.Cleared -= OnCleared;
            _core.StateChanged -= ApplyEnabled;
            _core.ConsoleFontChanged -= ApplyFont;
        }
        base.Dispose(disposing);
    }

    /// <summary>Uses the font chosen in Settings, and makes the command bar tall enough for it.</summary>
    private void ApplyFont()
    {
        _box.Font = _core.ConsoleFont;
        if (!_showInput) return;
        _input.Box.Font = _core.ConsoleFont;
        if (_bar is not null) _bar.Height = Math.Max(52, _input.Box.PreferredHeight + 42);
    }

    private void OnAdded(LogEntry e) => Append(e);
    private void OnCleared() => _box.Clear();

    private void ApplyEnabled()
    {
        if (!_showInput) return;
        var on = _core.State == ConnState.Connected;
        _input.Box.Enabled = on;
        _send.Enabled = on;
    }

    private async Task SendAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        _history.Add(text);
        _historyIndex = _history.Count;
        _input.Text = "";
        await _core.SendCommandAsync(_chat ? "say " + text : text);
    }

    private void InputKeyDown(object? s, KeyEventArgs e)
    {
        var box = _input.Box;
        if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SendAsync(); }
        else if (e.KeyCode == Keys.Up && _historyIndex > 0) { box.Text = _history[--_historyIndex]; box.SelectionStart = box.TextLength; e.Handled = true; }
        else if (e.KeyCode == Keys.Down && _historyIndex < _history.Count)
        {
            _historyIndex++;
            box.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
            box.SelectionStart = box.TextLength;
            e.Handled = true;
        }
    }

    internal static Color ColorOf(LogKind k) => k switch
    {
        LogKind.Chat => Theme.Chat,
        LogKind.Warning => Theme.Warn,
        LogKind.Error => Theme.Err,
        LogKind.Command => Theme.Accent,
        LogKind.Info => Theme.Muted,
        _ => Theme.Text,
    };

    private void Append(LogEntry e)
    {
        if (_chat && e.Kind != LogKind.Chat) return;
        if (_box.TextLength > 400_000) { _box.Select(0, _box.TextLength / 2); _box.SelectedText = ""; }
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = Theme.Dim;
        _box.AppendText(e.Time.ToString("HH:mm:ss") + "  ");
        _box.SelectionColor = ColorOf(e.Kind);
        _box.AppendText(e.Text + Environment.NewLine);
        if (_autoScroll.Checked) _box.ScrollToCaret();
    }
}

/// <summary>Live player table with right-click copy / kick / ban.</summary>
public class PlayersView : Panel
{
    private readonly Core _core;
    private readonly DataGridView _grid = new DarkGrid();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 6, 0, 0) };

    public Control[] HeaderActions => new Control[] { _count };

    public PlayersView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("SteamId", "Steam ID");
        _grid.Columns.Add("Ping", "Ping");
        _grid.Columns.Add("Time", "Time");
        _grid.Columns.Add("Health", "HP");
        _grid.Columns[0].FillWeight = 110;
        _grid.Columns[1].FillWeight = 125;
        _grid.Columns[2].FillWeight = 58;
        _grid.Columns[3].FillWeight = 84;
        _grid.Columns[4].FillWeight = 66;
        Controls.Add(_grid);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy Steam ID", null, (_, _) => { if (Selected() is { } p) Clipboard.SetText(p.SteamId); });
        menu.Items.Add("Kick...", null, (_, _) => Act("kick", "Kick"));
        menu.Items.Add("Ban...", null, (_, _) => Act("ban", "Ban"));
        _grid.ContextMenuStrip = menu;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _grid.ClearSelection(); _grid.Rows[e.RowIndex].Selected = true; }
        };

        _core.Monitor.Updated += Refresh;
        Fill();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Monitor.Updated -= Refresh;
        base.Dispose(disposing);
    }

    private PlayerInfo? Selected() =>
        _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as PlayerInfo : null;

    private void Act(string verb, string label)
    {
        if (Selected() is not { } p) return;
        var ok = MessageBox.Show(FindForm(), $"{label} {p.Name} ({p.SteamId})?", $"{label} player",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;
        var cmd = verb == "ban" ? $"banid {p.SteamId} \"{p.Name}\" \"Banned by admin\"" : $"kick {p.SteamId} \"Kicked by admin\"";
        _ = _core.SendCommandAsync(cmd);
    }

    private new void Refresh() => Fill();

    private void Fill()
    {
        var selectedId = Selected()?.SteamId;
        var players = _core.Monitor.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var p in players)
        {
            var idx = _grid.Rows.Add(p.Name, p.SteamId, p.Ping + "ms", Duration(p.ConnectedSeconds), Math.Round(p.Health).ToString());
            _grid.Rows[idx].Tag = p;
            if (p.SteamId == selectedId) _grid.Rows[idx].Selected = true;
        }
        _grid.ResumeLayout();
        _count.Text = _core.State == ConnState.Connected ? $"{players.Count} online" : "Not connected";
    }

    public static string Duration(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }
}

/// <summary>A grid of live server stat tiles.</summary>
public class ServerInfoView : Panel
{
    private readonly Core _core;
    private readonly (Label Value, Label Sub)[] _tiles;
    private static readonly string[] Captions = { "PLAYERS", "FPS", "ENTITIES", "UPTIME", "MEMORY", "MAP" };

    public ServerInfoView(Core core, int columns)
    {
        _core = core;
        BackColor = Theme.Panel;
        AutoScroll = true;
        DarkScroll.Apply(this);

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, BackColor = Theme.Panel };
        for (var c = 0; c < columns; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));

        _tiles = new (Label, Label)[Captions.Length];
        for (var i = 0; i < Captions.Length; i++)
        {
            var tile = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Input, Margin = new Padding(0, 0, 8, 6), Padding = new Padding(14, 10, 8, 6) };
            var value = new Label { Text = "-", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Size = new Size(120, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(12, 21) };
            var sub = new Label { Text = "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Size = new Size(120, 18), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(14, 47) };
            tile.Controls.Add(sub);
            tile.Controls.Add(value);
            tile.Controls.Add(new Label { Text = Captions[i], Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(14, 8) });
            table.Controls.Add(tile, i % columns, i / columns);
            _tiles[i] = (value, sub);
        }
        Controls.Add(table);

        _core.Monitor.Updated += UpdateTiles;
        UpdateTiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Monitor.Updated -= UpdateTiles;
        base.Dispose(disposing);
    }

    private void UpdateTiles()
    {
        var i = _core.Monitor.Info;
        if (i is null)
        {
            foreach (var (v, s) in _tiles) { v.Text = "-"; s.Text = ""; }
            return;
        }
        _tiles[0].Value.Text = $"{i.Players} / {i.MaxPlayers}";
        _tiles[0].Sub.Text = $"{i.Queued} queued, {i.Joining} joining";
        _tiles[1].Value.Text = i.Fps.ToString("0");
        _tiles[1].Sub.Text = "server frame rate";
        _tiles[2].Value.Text = i.Entities.ToString("N0");
        _tiles[2].Sub.Text = "world entities";
        var up = TimeSpan.FromSeconds(i.UptimeSeconds);
        _tiles[3].Value.Text = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m";
        _tiles[3].Sub.Text = "since last restart";
        _tiles[4].Value.Text = i.MemoryMb + " MB";
        _tiles[4].Sub.Text = "in use";
        _tiles[5].Value.Text = string.IsNullOrEmpty(i.Map) ? "-" : i.Map;
        _tiles[5].Sub.Text = i.Hostname;
        foreach (var (v, s) in _tiles) { Tips.Set(v, v.Text); Tips.Set(s, s.Text); }
    }
}

/// <summary>Builds a widget (title, content, header actions) for a WidgetKind.</summary>
public static class Widgets
{
    public static readonly WidgetKind[] All =
    {
        WidgetKind.Console, WidgetKind.Chat, WidgetKind.Players, WidgetKind.ServerInfo, WidgetKind.GiveItems,
        WidgetKind.Announce, WidgetKind.QuickActions, WidgetKind.Moderation, WidgetKind.Teleport, WidgetKind.Shortcuts,
    };

    public static string Name(WidgetKind k) => k switch
    {
        WidgetKind.Console => "Console",
        WidgetKind.Chat => "Chat",
        WidgetKind.Players => "Players",
        WidgetKind.GiveItems => "Give items",
        WidgetKind.Announce => "Announce",
        WidgetKind.QuickActions => "Quick actions",
        WidgetKind.Moderation => "Moderation",
        WidgetKind.Teleport => "Teleport",
        WidgetKind.Shortcuts => "Shortcuts",
        WidgetKind.Custom => "Community widget",
        _ => "Server info",
    };

    /// <summary>Returns the widget's content and the actions its card should show.</summary>
    public static (Control Content, Control[] Actions) Create(WidgetKind kind, Core core, int infoColumns = 2, string? customId = null)
    {
        switch (kind)
        {
            case WidgetKind.Chat: { var v = new ConsoleView(core, chat: true); return (v, v.HeaderActions); }
            case WidgetKind.Players: { var v = new PlayersView(core); return (v, v.HeaderActions); }
            case WidgetKind.ServerInfo: return (new ServerInfoView(core, infoColumns), Array.Empty<Control>());
            case WidgetKind.GiveItems: return (new GiveItemsView(core), Array.Empty<Control>());
            case WidgetKind.Announce: return (new AnnounceView(core), Array.Empty<Control>());
            case WidgetKind.QuickActions: return (new QuickActionsView(core), Array.Empty<Control>());
            case WidgetKind.Moderation: return (new ModerationView(core), Array.Empty<Control>());
            case WidgetKind.Teleport: return (new TeleportView(core), Array.Empty<Control>());
            case WidgetKind.Shortcuts: return (new ShortcutsView(core), Array.Empty<Control>());
            case WidgetKind.Custom:
                return core.Widgets.Get(customId) is { } def
                    ? (new CustomWidgetView(core, def), Array.Empty<Control>())
                    : (new Label { Text = "This community widget is not installed any more. Pick another widget from the title menu, or add it again under Tools, Widgets.", ForeColor = Theme.Muted, Padding = new Padding(4, 14, 4, 0) }, Array.Empty<Control>());
            default: { var v = new ConsoleView(core); return (v, v.HeaderActions); }
        }
    }

    public static Card CreateCard(WidgetKind kind, Core core, int infoColumns = 2)
    {
        var (content, actions) = Create(kind, core, infoColumns);
        return Card.Titled(Name(kind), content, actions);
    }
}
