using System.Text.Json;

namespace Bunker;

public class PlayerRecord
{
    public string SteamId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public double TotalSeconds { get; set; }
    public int Sessions { get; set; }
    public string Note { get; set; } = "";
}

public record ActivityEvent(DateTime Time, bool Joined, string Name, string SteamId);

/// <summary>
/// Remembers every player seen on the active server (first/last seen, time played, sessions, your notes)
/// and notices joins and leaves by comparing each player list with the last one. Saved on this PC, one file per server.
/// </summary>
public class PlayerTracker
{
    private const int MaxEvents = 500;
    private readonly Core _core;
    private readonly Dictionary<string, PlayerRecord> _records = new();
    private readonly HashSet<string> _online = new();
    private readonly List<ActivityEvent> _events = new();
    private DateTime _lastPollUtc = DateTime.UtcNow;
    private bool _baseline = true;
    private bool _dirty;
    private DateTime _lastSaveUtc = DateTime.UtcNow;
    private string? _loadedFor;

    public event Action? Changed;
    public event Action<ActivityEvent>? ActivityAdded;

    public IReadOnlyCollection<PlayerRecord> Records => _records.Values;
    public IReadOnlyList<ActivityEvent> Events => _events;
    public bool IsOnline(string steamId) => _online.Contains(steamId);

    public PlayerTracker(Core core)
    {
        _core = core;
        _core.Monitor.Updated += OnUpdated;
        _core.StateChanged += () => { if (_core.State != ConnState.Connected) { _online.Clear(); _baseline = true; Save(); Changed?.Invoke(); } };
        _core.ServerChanged += () => { Save(); Load(); Changed?.Invoke(); };
        Load();
    }

    private string? FilePath()
    {
        var id = _core.Settings.ActiveServer?.Id;
        return id is null ? null : Path.Combine(AppSettings.Folder, "players", id + ".json");
    }

    private void Load()
    {
        _records.Clear();
        _online.Clear();
        _events.Clear();
        _baseline = true;
        _loadedFor = _core.Settings.ActiveServer?.Id;
        try
        {
            if (FilePath() is { } path && File.Exists(path))
                foreach (var r in JsonSerializer.Deserialize<List<PlayerRecord>>(File.ReadAllText(path)) ?? new())
                    if (!string.IsNullOrEmpty(r.SteamId)) _records[r.SteamId] = r;
        }
        catch { /* a damaged file just starts the list afresh */ }
        _dirty = false;
    }

    public void Save()
    {
        if (!_dirty || FilePath() is not { } path || _loadedFor != _core.Settings.ActiveServer?.Id) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_records.Values.OrderBy(r => r.Name).ToList(), new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch { }
    }

    public void SetNote(string steamId, string note)
    {
        if (!_records.TryGetValue(steamId, out var r) || r.Note == note) return;
        r.Note = note;
        _dirty = true;
        Save();
        Changed?.Invoke();
    }

    private void OnUpdated()
    {
        if (_core.State != ConnState.Connected || _core.Monitor.Info is null) return;
        var now = DateTime.UtcNow;
        var elapsed = _baseline ? 0 : Math.Min(30, (now - _lastPollUtc).TotalSeconds);
        _lastPollUtc = now;

        var current = _core.Monitor.Players.Where(p => p.SteamId.Length > 0).ToList();
        var currentIds = current.Select(p => p.SteamId).ToHashSet();

        foreach (var p in current)
        {
            var isNew = !_records.TryGetValue(p.SteamId, out var record);
            record ??= _records[p.SteamId] = new PlayerRecord { SteamId = p.SteamId, FirstSeenUtc = now, Sessions = 0 };
            record.Name = string.IsNullOrWhiteSpace(p.Name) ? record.Name : p.Name;
            record.LastSeenUtc = now;
            record.TotalSeconds += elapsed;

            if (!_online.Contains(p.SteamId))
            {
                // Players already online when Bunker connected are not "joins"; they just become known.
                if (!_baseline) AddEvent(new ActivityEvent(DateTime.Now, true, record.Name, p.SteamId));
                if (!_baseline || isNew) record.Sessions++;
                _online.Add(p.SteamId);
            }
            _dirty = true;
        }

        foreach (var gone in _online.Where(id => !currentIds.Contains(id)).ToList())
        {
            _online.Remove(gone);
            if (_records.TryGetValue(gone, out var record)) AddEvent(new ActivityEvent(DateTime.Now, false, record.Name, gone));
        }

        _baseline = false;
        if ((now - _lastSaveUtc).TotalSeconds > 60) Save();
        Changed?.Invoke();
    }

    private void AddEvent(ActivityEvent e)
    {
        _events.Add(e);
        if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
        ActivityAdded?.Invoke(e);
    }
}
