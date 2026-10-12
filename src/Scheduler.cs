using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>Runs lists of commands, understanding "wait N" lines that pause for N seconds.</summary>
public static class CommandRunner
{
    private static readonly Regex Wait = new(@"^wait\s+(\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryWaitOnly(string command) => TryWait(command, out _);

    public static bool TryWait(string command, out int seconds)
    {
        var m = Wait.Match(command.Trim());
        seconds = m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 1, 3600) : 0;
        return m.Success;
    }

    public static async Task RunAsync(Core core, IEnumerable<string> commands, CancellationToken ct = default)
    {
        foreach (var raw in commands)
        {
            var command = raw.Trim();
            if (command.Length == 0) continue;
            if (TryWait(command, out var seconds)) { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); continue; }
            if (core.State != ConnState.Connected) return;
            await core.SendCommandAsync(command);
        }
    }
}

/// <summary>Runs the saved schedules while Bunker is open and connected. Runs missed while it was closed are not caught up.</summary>
public class Scheduler : IDisposable
{
    private readonly Core _core;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly HashSet<string> _running = new();
    private DateTime _connectedAt = DateTime.UtcNow;

    public event Action? Changed;

    public Scheduler(Core core)
    {
        _core = core;
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    public bool IsRunning(string id) => _running.Contains(id);

    public void OnConnected() { _connectedAt = DateTime.UtcNow; Changed?.Invoke(); }

    public static bool TryParseTime(string text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text.Trim(), new[] { "h\\:mm", "hh\\:mm" }, null, out time) && time < TimeSpan.FromDays(1);

    /// <summary>When the task will next run (local time), or null if it is switched off.</summary>
    public DateTime? NextRun(ScheduledTask t)
    {
        if (!t.Enabled) return null;
        if (t.Mode == "daily")
        {
            if (!TryParseTime(t.TimeOfDay, out var tod)) return null;
            var today = DateTime.Today + tod;
            return today > DateTime.Now ? today : today.AddDays(1);
        }
        var reference = new[] { t.LastRunUtc ?? DateTime.MinValue, _connectedAt }.Max();
        return reference.ToLocalTime().AddMinutes(Math.Max(1, t.IntervalMinutes));
    }

    private bool IsDue(ScheduledTask t)
    {
        if (!t.Enabled || _running.Contains(t.Id)) return false;
        if (t.Mode == "daily")
        {
            if (!TryParseTime(t.TimeOfDay, out var tod)) return false;
            var target = DateTime.Today + tod;
            var now = DateTime.Now;
            var lastLocal = t.LastRunUtc?.ToLocalTime() ?? DateTime.MinValue;
            return now >= target && now < target.AddMinutes(15) && lastLocal < target && _connectedAt.ToLocalTime() < target.AddMinutes(15);
        }
        return DateTime.Now >= NextRun(t);
    }

    private async Task TickAsync()
    {
        if (_core.State != ConnState.Connected) return;
        foreach (var task in _core.Settings.Schedules.ToList())
            if (IsDue(task)) await RunAsync(task);
        Changed?.Invoke();
    }

    public async Task RunAsync(ScheduledTask task)
    {
        if (!_running.Add(task.Id)) return;
        try
        {
            Changed?.Invoke();
            _core.Log.Add($"Schedule \"{task.Name}\" is running...", LogKind.Info);
            await CommandRunner.RunAsync(_core, task.Commands);
        }
        finally
        {
            task.LastRunUtc = DateTime.UtcNow;
            _running.Remove(task.Id);
            try { _core.Settings.Save(); } catch { }
            Changed?.Invoke();
        }
    }

    public void Dispose() => _timer.Dispose();
}
