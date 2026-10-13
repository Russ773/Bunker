namespace Bunker;

public enum WatchState { Idle, Connecting, Online, Offline }

/// <summary>Plain-language text for the multi-server overview. Kept separate so it can be tested.</summary>
public static class WatchText
{
    /// <summary>Turns a connection error into something a person can act on.</summary>
    public static string Describe(string? message)
    {
        var m = (message ?? "").Trim();
        if (m.Length == 0) return "Could not connect";
        if (m.Contains("refused", StringComparison.OrdinalIgnoreCase)) return "Connection refused: is RCON switched on, and is the port right?";
        if (m.Contains("No such host", StringComparison.OrdinalIgnoreCase) || m.Contains("host could not be found", StringComparison.OrdinalIgnoreCase)
            || m.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)) return "Host not found: check the address";
        if (m.Contains("timed out", StringComparison.OrdinalIgnoreCase) || m.Contains("did not properly respond", StringComparison.OrdinalIgnoreCase)) return "No answer (timed out)";
        if (m.Contains("401") || m.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)) return "Wrong RCON password";
        var first = m.Split('\n')[0].Trim();
        return first.Length <= 90 ? first : first[..89] + "…";
    }

    /// <summary>"Connection refused..." plus when the next try is, without doubling up the full stop.</summary>
    public static string WithRetry(string reason, int seconds) =>
        (reason.TrimEnd().EndsWith('?') || reason.TrimEnd().EndsWith('.') || reason.TrimEnd().EndsWith('!') ? reason.TrimEnd() + " " : reason.TrimEnd() + ". ") + $"Trying again in {seconds}s";

    /// <summary>The line above the cards, such as "3 servers · 2 online · 41 players".</summary>
    public static string Summary(int servers, int online, int players)
    {
        if (servers == 0) return "No servers saved yet";
        var parts = new List<string> { servers == 1 ? "1 server" : $"{servers} servers", $"{online} online" };
        if (online > 0) parts.Add(players == 1 ? "1 player" : $"{players} players");
        return string.Join("  ·  ", parts);
    }

    /// <summary>Servers as a short list for a confirmation message: "A, B and C".</summary>
    public static string NameList(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>Who is on, as a short line for a card: "Ironclad, RustLord and 3 more".</summary>
    public static string PlayerLine(IReadOnlyList<string> names, int show = 3)
    {
        var clean = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (clean.Count == 0) return "";
        if (clean.Count <= show) return string.Join(", ", clean);
        return string.Join(", ", clean.Take(show)) + $" and {clean.Count - show} more";
    }
}

/// <summary>
/// Keeps one saved server's status up to date with its own connection, separate from the one the rest of Bunker uses, so the overview can
/// show every server at once. It polls <c>serverinfo</c> and <c>playerlist</c>, and reconnects by itself when a server goes away and comes back.
/// </summary>
public sealed class ServerWatcher : IDisposable
{
    private const int PollSeconds = 10;
    private const int HistoryLength = 60;
    private readonly RconClient _rcon = new();
    private readonly List<int> _history = new();
    private CancellationTokenSource? _cts;

    public ServerProfile Profile { get; }
    public WatchState State { get; private set; } = WatchState.Idle;
    /// <summary>Why the server is offline, or when the next try is.</summary>
    public string Detail { get; private set; } = "";
    public ServerInfo? Info { get; private set; }
    public IReadOnlyList<PlayerInfo> Players { get; private set; } = Array.Empty<PlayerInfo>();
    /// <summary>The player count at each recent poll, oldest first (for the little chart on a card).</summary>
    public IReadOnlyList<int> PlayerHistory => _history;

    /// <summary>Raised whenever something changed. It can come from any thread.</summary>
    public event Action? Changed;

    public ServerWatcher(ServerProfile profile) => Profile = profile;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    private void Set(WatchState state, string detail = "")
    {
        State = state;
        Detail = detail;
        Changed?.Invoke();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            Set(WatchState.Connecting);
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                    await _rcon.ConnectAsync(Profile.Host, Profile.Port, Profile.Password, connectTimeout.Token);
                }
                attempt = 0;
                await PollAsync(); // sets Online once there is something to show
                while (!ct.IsCancellationRequested && _rcon.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollSeconds), ct);
                    await PollAsync();
                }
                Detail = "Connection lost";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { Detail = "No answer (timed out)"; }
            catch (Exception e) { Detail = WatchText.Describe(e.InnerException?.Message ?? e.Message); }

            Info = null;
            Players = Array.Empty<PlayerInfo>();
            var wait = ReconnectPlan.DelaySeconds(++attempt);
            var reason = Detail;
            for (var left = wait; left > 0 && !ct.IsCancellationRequested; left--)
            {
                Set(WatchState.Offline, WatchText.WithRetry(reason, left));
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task PollAsync()
    {
        try
        {
            var info = ServerMonitor.ParseInfo(await _rcon.QueryAsync("serverinfo", 6000));
            var players = ServerMonitor.ParsePlayers(await _rcon.QueryAsync("playerlist", 6000));
            if (info is null) return;
            Info = info;
            Players = players;
            _history.Add(info.Players);
            if (_history.Count > HistoryLength) _history.RemoveAt(0);
            Set(WatchState.Online);
        }
        catch { /* a missed poll is fine; if the connection is gone the loop notices */ }
    }

    /// <summary>Runs a command on this server. Returns the server's reply, or null if it could not be sent.</summary>
    public async Task<string?> SendAsync(string command)
    {
        if (State != WatchState.Online || !_rcon.IsConnected) return null;
        try { return await _rcon.QueryAsync(command, 8000); }
        catch { return null; }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _ = _rcon.DisconnectAsync();
        State = WatchState.Idle;
    }

    public void Dispose()
    {
        Stop();
        _rcon.Dispose();
    }
}
