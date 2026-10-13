using System.Text.Json;
using System.Windows.Forms;

namespace Bunker;

public enum LogKind { Info, Normal, Chat, Warning, Error, Command }

/// <summary>One console line. For chat, UserId/UserName/Message say who said what.</summary>
public record LogEntry(DateTime Time, string Text, LogKind Kind, string? UserId = null, string? UserName = null, string? Message = null, string? Color = null, int Channel = 0, string? Tags = null);

/// <summary>Everything the console has shown, shared by every console/chat view so they stay in sync.</summary>
public class ConsoleLog
{
    private const int MaxEntries = 5000;
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;
    public event Action<LogEntry>? Added;
    public event Action? Cleared;

    public void Add(string text, LogKind kind = LogKind.Normal, string? userId = null, string? userName = null, string? message = null, string? color = null, int channel = 0, string? tags = null)
    {
        var e = new LogEntry(DateTime.Now, text, kind, userId, userName, message, color, channel, tags);
        _entries.Add(e);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        Added?.Invoke(e);
    }

    public void Clear() { _entries.Clear(); Cleared?.Invoke(); }
}

public record ServerInfo(string Hostname, int Players, int MaxPlayers, int Queued, int Joining, int Entities,
    double Fps, long UptimeSeconds, long MemoryMb, string Map);

/// <summary>One reading of the server taken by the monitor (used for the health charts).</summary>
public record HealthSample(DateTime Time, int Players, double Fps, int Entities, long MemoryMb, long UptimeSeconds);

public record PlayerInfo(string SteamId, string Name, int Ping, string Address, long ConnectedSeconds, double Health);

/// <summary>Polls serverinfo and playerlist while connected and publishes the results.</summary>
public class ServerMonitor : IDisposable
{
    private readonly RconClient _rcon;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private bool _busy;

    public ServerInfo? Info { get; private set; }
    public IReadOnlyList<PlayerInfo> Players { get; private set; } = Array.Empty<PlayerInfo>();

    private readonly List<HealthSample> _history = new();
    private const int MaxSamples = 1440; // two hours at one reading every 5 seconds

    /// <summary>Recent readings, oldest first.</summary>
    public IReadOnlyList<HealthSample> History => _history;
    public void ClearHistory() { _history.Clear(); RaiseUpdated(); }

    /// <summary>Tells every listener, so one listener failing cannot stop the others (or the next poll). Failures are shown in debug builds.</summary>
    private void RaiseUpdated()
    {
        if (Updated is null) return;
        foreach (var listener in Updated.GetInvocationList().Cast<Action>())
        {
            try { listener(); }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine("Monitor listener failed: " + e); }
        }
    }

#if DEBUG
    /// <summary>Test hook: fills the history with believable readings so charts can be checked without waiting.</summary>
    public void SeedForTest()
    {
        var rnd = new Random(7);
        _history.Clear();
        for (var i = 0; i < 260; i++)
        {
            var t = DateTime.Now.AddSeconds(-(260 - i) * 5);
            var wave = Math.Sin(i / 22.0);
            _history.Add(new HealthSample(t, (int)Math.Round(14 + wave * 6 + rnd.Next(-1, 2)), Math.Min(60, 57 + rnd.NextDouble() * 3 - (i is > 120 and < 135 ? 22 : 0)),
                180_000 + i * 55 + rnd.Next(-300, 300), 6100 + i * 2 + rnd.Next(-20, 20), 40_000 + i * 5));
        }
        RaiseUpdated();
    }
#endif
    public event Action? Updated;

    public ServerMonitor(RconClient rcon)
    {
        _rcon = rcon;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public void Start() { _timer.Start(); _ = RefreshAsync(); }

    public void Stop()
    {
        _timer.Stop();
        Info = null;
        Players = Array.Empty<PlayerInfo>();
        RaiseUpdated();
    }

    public async Task RefreshAsync()
    {
        if (_busy || !_rcon.IsConnected) return;
        _busy = true;
        try
        {
            var info = ParseInfo(await _rcon.QueryAsync("serverinfo"));
            var players = ParsePlayers(await _rcon.QueryAsync("playerlist"));
            if (info is not null)
            {
                Info = info;
                _history.Add(new HealthSample(DateTime.Now, info.Players, info.Fps, info.Entities, info.MemoryMb, info.UptimeSeconds));
                if (_history.Count > MaxSamples) _history.RemoveRange(0, _history.Count - MaxSamples);
            }
            Players = players;
            RaiseUpdated();
        }
        catch { /* a missed poll is fine; the next tick tries again */ }
        finally { _busy = false; }
    }

    internal static ServerInfo? ParseInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new ServerInfo(
                Str(r, "Hostname"), (int)Num(r, "Players"), (int)Num(r, "MaxPlayers"), (int)Num(r, "Queued"),
                (int)Num(r, "Joining"), (int)Num(r, "EntityCount"), Num(r, "Framerate"), (long)Num(r, "Uptime"),
                (long)Num(r, "Memory"), Str(r, "Map"));
        }
        catch { return null; }
    }

    internal static IReadOnlyList<PlayerInfo> ParsePlayers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<PlayerInfo>();
            foreach (var p in doc.RootElement.EnumerateArray())
                list.Add(new PlayerInfo(Str(p, "SteamID"), Str(p, "DisplayName"), (int)Num(p, "Ping"),
                    Str(p, "Address"), (long)Num(p, "ConnectedSeconds"), Num(p, "Health")));
            return list;
        }
        catch { return Array.Empty<PlayerInfo>(); }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()) : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public void Dispose() => _timer.Dispose();
}

public enum ConnState { Disconnected, Connecting, Connected }

/// <summary>The one connection, console history, monitor and settings that every page and widget shares.</summary>
public class Core
{
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();

    public AppSettings Settings { get; }
    public RconClient Rcon { get; } = new();
    public WidgetStore Widgets { get; } = new();
    public Scheduler Scheduler { get; }
    public ItemCatalog Items { get; }
    public WidgetUpdateService WidgetUpdates { get; }
    public PlayerTracker Tracker { get; }
    public ConsoleLog Log { get; } = new();
    public ServerMonitor Monitor { get; }

    public ConnState State { get; private set; } = ConnState.Disconnected;
    public string StatusText { get; private set; } = "Disconnected";
    public event Action? StateChanged;
    public event Action? ServerChanged;
    public event Action? ConsoleFontChanged;

    private Font? _consoleFont;

    /// <summary>The font used by the console and chat views (chosen in Settings).</summary>
    public Font ConsoleFont => _consoleFont ??= new Font(Settings.ConsoleFontName, Math.Clamp(Settings.ConsoleFontSize, 7f, 32f));

    public void SetConsoleFont(string name, float size)
    {
        Settings.ConsoleFontName = name;
        Settings.ConsoleFontSize = Math.Clamp(size, 7f, 32f);
        Settings.Save();
        _consoleFont = null;
        ConsoleFontChanged?.Invoke();
    }

    public Core(AppSettings settings)
    {
        Settings = settings;
        Monitor = new ServerMonitor(Rcon);
        Scheduler = new Scheduler(this);
        Items = new ItemCatalog(() => settings.UseOnlineItemList);
        WidgetUpdates = new WidgetUpdateService(this);
        Tracker = new PlayerTracker(this);
        Rcon.MessageReceived += m => _ui.Post(_ => OnMessage(m), null);
        Rcon.Disconnected += reason => _ui.Post(_ =>
        {
            var wasConnected = State == ConnState.Connected;
            Log.Add(reason, LogKind.Info);
            SetState(ConnState.Disconnected, reason);
            if (wasConnected) { ConnectionLost?.Invoke(); StartReconnect(); } // the connection dropped (the user did not disconnect): try to get it back
        }, null);
    }

    /// <summary>Adds or updates a saved server. A brand-new server becomes the active one only if none was active.</summary>
    public void SaveServer(ServerProfile server, bool makeActive = false)
    {
        Settings.AddOrUpdateServer(server, makeActive);
        Settings.Save();
        ServerChanged?.Invoke();
    }

    /// <summary>Removes a saved server (and disconnects first if it is the one in use).</summary>
    public async Task DeleteServerAsync(string id)
    {
        var wasActive = Settings.ActiveServer?.Id == id;
        if (wasActive) CancelReconnect();
        if (wasActive && State != ConnState.Disconnected) await DisconnectAsync();
        Settings.RemoveServer(id);
        Settings.Save();
        ServerChanged?.Invoke();
    }

    /// <summary>Makes a saved server the active one and connects to it.</summary>
    public async Task SwitchServerAsync(string id)
    {
        if (Settings.Servers.All(s => s.Id != id)) return;
        CancelReconnect();
        if (State != ConnState.Disconnected) await DisconnectAsync();
        Settings.ActiveServerId = id;
        Monitor.ClearHistory();
        Settings.Save();
        Log.Clear();
        ServerChanged?.Invoke();
        await ConnectAsync();
    }


    /// <summary>Connects to the active server. Doing this yourself ends any automatic reconnecting.</summary>
    public async Task ConnectAsync()
    {
        CancelReconnect();
        await ConnectCoreAsync(logFailure: true);
    }

    private async Task ConnectCoreAsync(bool logFailure)
    {
        var s = Settings.ActiveServer;
        if (s is null || State == ConnState.Connecting) return;
        SetState(ConnState.Connecting, "Connecting...");
        try
        {
            await Rcon.ConnectAsync(s.Host, s.Port, s.Password);
            Log.Add($"Connected to {s.DisplayName} ({s.Host}:{s.Port})", LogKind.Info);
            SetState(ConnState.Connected, "Connected");
            Monitor.Start();
        }
        catch (Exception e)
        {
            if (logFailure) Log.Add("Connection failed: " + e.Message, LogKind.Error);
            SetState(ConnState.Disconnected, "Connection failed");
        }
    }

    public async Task DisconnectAsync()
    {
        CancelReconnect();
        await Rcon.DisconnectAsync();
        SetState(ConnState.Disconnected, "Disconnected");
    }

    // ---------------------------------------------------------------- Reconnecting

    private CancellationTokenSource? _reconnect;

    /// <summary>Raised when an established connection drops by itself (not when you disconnect).</summary>
    public event Action? ConnectionLost;
    /// <summary>Raised when automatic reconnecting succeeds.</summary>
    public event Action? Reconnected;

    /// <summary>Raised when the tray icon settings change, so the icon shows or hides straight away.</summary>
    public event Action? TraySettingsChanged;
    public void NotifyTraySettingsChanged() => TraySettingsChanged?.Invoke();

    /// <summary>True while Bunker is waiting to retry after the connection dropped.</summary>
    public bool Reconnecting => _reconnect is not null;

    private void StartReconnect()
    {
        if (!Settings.AutoReconnect || Settings.ActiveServer is null || _reconnect is not null) return;
        var cts = _reconnect = new CancellationTokenSource();
        _ = ReconnectLoopAsync(cts);
    }

    private void CancelReconnect()
    {
        var cts = _reconnect;
        _reconnect = null;
        cts?.Cancel();
    }

    private async Task ReconnectLoopAsync(CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
            {
                for (var left = ReconnectPlan.DelaySeconds(attempt); left > 0; left--)
                {
                    SetState(ConnState.Disconnected, $"Reconnecting in {left}s (attempt {attempt})");
                    await Task.Delay(1000, ct);
                }
                if (attempt == 1) Log.Add("Connection lost. Trying to reconnect automatically (click Connect or Disconnect to stop).", LogKind.Warning);
                await ConnectCoreAsync(logFailure: false);
                if (ct.IsCancellationRequested) return;
                if (State == ConnState.Connected) { Log.Add("Reconnected.", LogKind.Info); Reconnected?.Invoke(); return; }
                if (ReconnectPlan.ShouldLogFailure(attempt)) Log.Add($"Still unable to reach the server (attempt {attempt}). Trying again...", LogKind.Info);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_reconnect, cts)) _reconnect = null;
            cts.Dispose();
        }
    }

    /// <summary>Raised when a widget is starred, un-starred, hidden or shown, so the sidebar and the Widgets page redraw.</summary>
    public event Action? WidgetPrefsChanged;
    public void NotifyWidgetPrefsChanged() => WidgetPrefsChanged?.Invoke();

    public void ToggleFavouriteWidget(string key)
    {
        if (!Settings.FavouriteWidgets.Remove(key)) Settings.FavouriteWidgets.Add(key);
        Settings.Save();
        NotifyWidgetPrefsChanged();
    }

    public void SetWidgetHidden(string key, bool hide)
    {
        WidgetGroups.SetHidden(Settings.FavouriteWidgets, Settings.HiddenWidgets, key, hide);
        Settings.Save();
        NotifyWidgetPrefsChanged();
    }

    /// <summary>Raised when the list of hidden console messages changes, so every console redraws.</summary>
    public event Action? ConsoleFilterChanged;
    public void NotifyConsoleFilterChanged() => ConsoleFilterChanged?.Invoke();

    /// <summary>Raised when your chat name, Steam ID or colour is changed in Settings.</summary>
    public event Action? ChatIdentityChanged;
    public void NotifyChatIdentityChanged() => ChatIdentityChanged?.Invoke();

    private bool _chatPluginMissing;

    /// <summary>
    /// Sends a message from the Chat panel. With a chat name set it goes through the Bunker Chat plugin (your name, colour and avatar in-game);
    /// if the plugin is not on the server, or no name is set, it uses the plain say command. Your own message is shown in the panel straight away.
    /// </summary>
    public async Task SendChatAsync(string text)
    {
        if (!Rcon.IsConnected || text.Length == 0) return;
        var name = Settings.ChatName.Trim();
        var colour = ChatSender.ColourOrDefault(Settings.ChatColor);

        if (name.Length > 0 && Settings.ChatUsePlugin && !_chatPluginMissing)
        {
            var command = ChatSender.PluginCommand(Settings.ChatSteamId, name, colour, text);
            try
            {
                var reply = await Rcon.QueryAsync(command, 6000);
                if (!ChatSender.LooksUnknown(reply))
                {
                    EchoChat(name, text, colour);
                    return;
                }
                _chatPluginMissing = true;
                Log.Add("The Bunker Chat plugin is not installed on this server, so chat is sent with the plain say command (it shows as SERVER). Settings → Chat explains how to install it.", LogKind.Warning);
            }
            catch (Exception e) { Log.Add("Chat was not sent: " + e.Message, LogKind.Error); return; }
        }

        // The message shows in the console as chat (with your name), so the command itself is not repeated there.
        await SendCommandAsync(ChatSender.SayCommand(name, text), showInConsole: false);
        if (name.Length > 0) EchoChat(name, text, colour);
        else Log.Add("SERVER: " + text, LogKind.Chat, null, "SERVER", text, "#eee", 0);
    }

    private void EchoChat(string name, string text, string colour)
    {
        var id = ChatSender.IsSteamId(Settings.ChatSteamId) ? Settings.ChatSteamId : null;
        Log.Add($"{name}: {text}", LogKind.Chat, id, name, text, colour, 0);
    }

    public async Task SendCommandAsync(string command, bool showInConsole = true)
    {
        if (!Rcon.IsConnected) return;
        if (showInConsole) Log.Add("> " + command, LogKind.Command);
        try { await Rcon.SendCommandAsync(command); }
        catch (Exception e) { Log.Add("Send failed: " + e.Message, LogKind.Error); }
    }

    private void SetState(ConnState state, string text)
    {
        State = state;
        StatusText = text;
        if (state == ConnState.Connected) _chatPluginMissing = false; // look again: the plugin may have been installed since
        if (state != ConnState.Connected) Monitor.Stop();
        else Scheduler.OnConnected();
        StateChanged?.Invoke();
    }

    private void OnMessage(RconMessage m)
    {
        var type = m.Type.ToLowerInvariant();
        if (type == "chat")
        {
            var (text, userId, name, message, color, channel, tags) = ParseChat(m.Message);
            Log.Add(text, LogKind.Chat, userId, name, message, color, channel, tags);
            return;
        }
        var kind = type switch { "warning" => LogKind.Warning, "error" => LogKind.Error, _ => LogKind.Normal };
        Log.Add(m.Message.TrimEnd('\n'), kind);
    }

    /// <summary>
    /// Newer Rust versions send chat as JSON: who said it (name, Steam ID and the name colour the game shows), the message, and the
    /// channel (0 = global, 1 = team). Turn it into "Name: message" plus those details.
    /// </summary>
    internal static (string Text, string? UserId, string? Name, string? Message, string? Color, int Channel, string? Tags) ParseChat(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var r = doc.RootElement;
            if (r.TryGetProperty("Username", out var u) && r.TryGetProperty("Message", out var msg))
            {
                var id = r.TryGetProperty("UserId", out var uid) ? (uid.ValueKind == JsonValueKind.String ? uid.GetString() : uid.GetRawText()) : null;
                var color = r.TryGetProperty("Color", out var col) && col.ValueKind == JsonValueKind.String ? col.GetString() : null;
                var channel = r.TryGetProperty("Channel", out var ch) && ch.ValueKind == JsonValueKind.Number && ch.TryGetInt32(out var n) ? n : 0;
                var who = ChatText.StripMarkup(u.GetString() ?? "");
                var (tags, said) = ChatText.SplitTags(who, ChatText.StripMarkup(msg.GetString() ?? ""));
                return ($"{(tags.Length > 0 ? tags + " " : "")}{who}: {said}", id, who, said, color, channel, tags.Length > 0 ? tags : null);
            }
        }
        catch { }
        var plain = raw.TrimEnd('\n');
        return (plain, null, null, plain, null, 0, null);
    }
}
