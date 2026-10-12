using System.Text.Json;
using System.Windows.Forms;

namespace Bunker;

public enum LogKind { Info, Normal, Chat, Warning, Error, Command }

public record LogEntry(DateTime Time, string Text, LogKind Kind);

/// <summary>Everything the console has shown, shared by every console/chat view so they stay in sync.</summary>
public class ConsoleLog
{
    private const int MaxEntries = 5000;
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;
    public event Action<LogEntry>? Added;
    public event Action? Cleared;

    public void Add(string text, LogKind kind = LogKind.Normal)
    {
        var e = new LogEntry(DateTime.Now, text, kind);
        _entries.Add(e);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        Added?.Invoke(e);
    }

    public void Clear() { _entries.Clear(); Cleared?.Invoke(); }
}

public record ServerInfo(string Hostname, int Players, int MaxPlayers, int Queued, int Joining, int Entities,
    double Fps, long UptimeSeconds, long MemoryMb, string Map);

public record PlayerInfo(string SteamId, string Name, int Ping, string Address, long ConnectedSeconds, double Health);

/// <summary>Polls serverinfo and playerlist while connected and publishes the results.</summary>
public class ServerMonitor : IDisposable
{
    private readonly RconClient _rcon;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private bool _busy;

    public ServerInfo? Info { get; private set; }
    public IReadOnlyList<PlayerInfo> Players { get; private set; } = Array.Empty<PlayerInfo>();
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
        Updated?.Invoke();
    }

    public async Task RefreshAsync()
    {
        if (_busy || !_rcon.IsConnected) return;
        _busy = true;
        try
        {
            var info = ParseInfo(await _rcon.QueryAsync("serverinfo"));
            var players = ParsePlayers(await _rcon.QueryAsync("playerlist"));
            if (info is not null) Info = info;
            Players = players;
            Updated?.Invoke();
        }
        catch { /* a missed poll is fine; the next tick tries again */ }
        finally { _busy = false; }
    }

    private static ServerInfo? ParseInfo(string json)
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

    private static IReadOnlyList<PlayerInfo> ParsePlayers(string json)
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
    public ConsoleLog Log { get; } = new();
    public ServerMonitor Monitor { get; }

    public ConnState State { get; private set; } = ConnState.Disconnected;
    public string StatusText { get; private set; } = "Disconnected";
    public event Action? StateChanged;
    public event Action? ServerChanged;

    public Core(AppSettings settings)
    {
        Settings = settings;
        Monitor = new ServerMonitor(Rcon);
        Rcon.MessageReceived += m => _ui.Post(_ => OnMessage(m), null);
        Rcon.Disconnected += reason => _ui.Post(_ =>
        {
            Log.Add(reason, LogKind.Info);
            SetState(ConnState.Disconnected, reason);
        }, null);
    }

    public void SetServer(ServerProfile? server)
    {
        Settings.Server = server;
        Settings.Save();
        ServerChanged?.Invoke();
    }

    public async Task ConnectAsync()
    {
        var s = Settings.Server;
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
            Log.Add("Connection failed: " + e.Message, LogKind.Error);
            SetState(ConnState.Disconnected, "Connection failed");
        }
    }

    public async Task DisconnectAsync()
    {
        await Rcon.DisconnectAsync();
        SetState(ConnState.Disconnected, "Disconnected");
    }

    public async Task SendCommandAsync(string command)
    {
        if (!Rcon.IsConnected) return;
        Log.Add("> " + command, LogKind.Command);
        try { await Rcon.SendCommandAsync(command); }
        catch (Exception e) { Log.Add("Send failed: " + e.Message, LogKind.Error); }
    }

    private void SetState(ConnState state, string text)
    {
        State = state;
        StatusText = text;
        if (state != ConnState.Connected) Monitor.Stop();
        StateChanged?.Invoke();
    }

    private void OnMessage(RconMessage m)
    {
        var type = m.Type.ToLowerInvariant();
        if (type == "chat") { Log.Add(FormatChat(m.Message), LogKind.Chat); return; }
        var kind = type switch { "warning" => LogKind.Warning, "error" => LogKind.Error, _ => LogKind.Normal };
        Log.Add(m.Message.TrimEnd('\n'), kind);
    }

    /// <summary>Newer Rust versions send chat as JSON; turn it into "Name: message".</summary>
    private static string FormatChat(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var r = doc.RootElement;
            if (r.TryGetProperty("Username", out var u) && r.TryGetProperty("Message", out var msg))
                return $"{u.GetString()}: {msg.GetString()}";
        }
        catch { }
        return raw.TrimEnd('\n');
    }
}
