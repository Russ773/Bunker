using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;

namespace Bunker;

/// <summary>Flashes the window's taskbar button to get attention.</summary>
public static class Notifier
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public static void Flash(Form? form)
    {
        if (form is null || !form.IsHandleCreated) return;
        // 0x3 = flash the caption and the taskbar button; 0xC = until the window comes to the front
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = form.Handle, Flags = 0x3 | 0xC, Count = uint.MaxValue };
        try { FlashWindowEx(ref info); } catch { }
    }
}

/// <summary>Chat with highlighted watch-words, alerts, and per-message actions (mute, kick, ban).</summary>
public class ChatMonitorView : Panel
{
    private const int MaxRows = 400;
    private readonly Core _core;
    private readonly DarkGrid _grid = new();
    private readonly ModernField _words = new();
    private readonly CheckBox _flash = new() { Text = "Flash the taskbar", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(0, 6, 18, 0) };
    private readonly CheckBox _sound = new() { Text = "Play a sound", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(0, 6, 18, 0) };
    private readonly Label _stats = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 9, 0, 0) };
    private int _matches;

    public ChatMonitorView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        var top = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Panel };
        top.Controls.Add(new Label { Text = "Words to watch for (separate with commas). Messages containing them are highlighted and raise an alert.", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Location = new Point(0, 4) });
        _words.Location = new Point(0, 28);
        _words.Width = 560;
        _words.Box.PlaceholderText = "for example: cheat, hack, discord.gg, free scrap";
        var options = new FlowLayoutPanel { Location = new Point(0, 62), Size = new Size(900, 32), BackColor = Theme.Panel, WrapContents = false };
        options.Controls.AddRange(new Control[] { _flash, _sound, _stats });
        top.Controls.AddRange(new Control[] { _words, options });

        foreach (var (key, title, weight) in new[] { ("Time", "Time", 55), ("Player", "Player", 110), ("Message", "Message", 420), ("Id", "Steam ID", 120) })
        {
            _grid.Columns.Add(key, title);
            _grid.Columns[key].FillWeight = weight;
        }
        _grid.Dock = DockStyle.Fill;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

        Controls.Add(_grid);
        Controls.Add(top);

        _words.Text = core.Settings.ChatWatchWords;
        _flash.Checked = core.Settings.ChatFlash;
        _sound.Checked = core.Settings.ChatSound;

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        void Item(string text, Action action) => menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => action()) { BackColor = Theme.Input, ForeColor = Theme.Text });
        Item("Mute this player", () => _ = ActAsync("mute"));
        Item("Kick this player...", () => _ = ActAsync("kick"));
        Item("Ban this player...", () => _ = ActAsync("ban"));
        menu.Items.Add(new ToolStripSeparator());
        Item("Copy message", () => { if (Selected()?.Message is { } m) Clipboard.SetText(m); });
        Item("Copy Steam ID", () => { if (Selected()?.UserId is { } id) Clipboard.SetText(id); });
        _grid.ContextMenuStrip = menu;
        _grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _grid.ClearSelection(); _grid.Rows[e.RowIndex].Selected = true; } };

        _words.Box.TextChanged += (_, _) => { _core.Settings.ChatWatchWords = _words.Text; _core.Settings.Save(); Rebuild(); };
        _flash.CheckedChanged += (_, _) => { _core.Settings.ChatFlash = _flash.Checked; _core.Settings.Save(); };
        _sound.CheckedChanged += (_, _) => { _core.Settings.ChatSound = _sound.Checked; _core.Settings.Save(); };
        _core.Log.Added += OnAdded;
        _core.Log.Cleared += Rebuild;
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _core.Log.Added -= OnAdded; _core.Log.Cleared -= Rebuild; }
        base.Dispose(disposing);
    }

    private List<string> Words() =>
        _words.Text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length > 0).ToList();

    private bool Matches(LogEntry e) => Words() is { Count: > 0 } words && words.Any(w => e.Text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private LogEntry? Selected() => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as LogEntry : null;

    private void Rebuild()
    {
        _grid.Rows.Clear();
        _matches = 0;
        foreach (var e in _core.Log.Entries.Where(e => e.Kind == LogKind.Chat).TakeLast(MaxRows)) AddRow(e, alert: false);
        _grid.ClearSelection();
        UpdateStats();
    }

    private void OnAdded(LogEntry e)
    {
        if (e.Kind != LogKind.Chat) return;
        AddRow(e, alert: true);
        if (_grid.SelectedRows.Count == 1 && _grid.Rows.Count > 1 && _grid.SelectedRows[0].Index == 0) _grid.ClearSelection();
        UpdateStats();
    }

    private void AddRow(LogEntry e, bool alert)
    {
        var message = e.Message ?? e.Text;
        var i = _grid.Rows.Add(e.Time.ToString("HH:mm:ss"), e.UserName ?? "", message, e.UserId ?? "");
        var row = _grid.Rows[i];
        row.Tag = e;
        row.DefaultCellStyle.ForeColor = Theme.Chat;
        if (Matches(e))
        {
            _matches++;
            row.DefaultCellStyle.BackColor = Theme.MatchBack;
            row.DefaultCellStyle.ForeColor = Theme.Warn;
            if (alert) Alert(e);
        }
        while (_grid.Rows.Count > MaxRows) _grid.Rows.RemoveAt(0);
        if (_grid.Rows.Count > 0 && _grid.SelectedRows.Count == 0) _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, _grid.Rows.Count - 1);
    }

    private void UpdateStats() =>
        _stats.Text = Words().Count == 0 ? "No watch-words set" : $"{_matches} message{(_matches == 1 ? "" : "s")} matched";

    private void Alert(LogEntry e)
    {
        if (_flash.Checked) Notifier.Flash(FindForm());
        if (_sound.Checked) SystemSounds.Exclamation.Play();
    }

    private async Task ActAsync(string verb)
    {
        if (Selected() is not { UserId: { Length: > 0 } id } e) { MessageBox.Show(FindForm(), "This message has no player ID, so it can't be acted on.", "No player ID"); return; }
        if (_core.State != ConnState.Connected) return;
        var name = e.UserName ?? id;
        var reason = verb == "ban" ? "Banned by admin" : "Kicked by admin";
        var title = verb == "ban" ? "Ban player" : verb == "mute" ? "Mute player" : "Kick player";
        var text = verb == "mute" ? $"Mute {name}?" : $"{(verb == "ban" ? "Ban" : "Kick")} {name}?\n\nMessage: {e.Message}";
        if (MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        switch (verb)
        {
            case "ban":
                await _core.SendCommandAsync($"banid {id} {Cmd.Quote(name)} {Cmd.Quote(reason)}");
                await _core.SendCommandAsync("server.writecfg");
                break;
            case "mute":
                await _core.SendCommandAsync($"mute \"{id}\"");
                break;
            default:
                await _core.SendCommandAsync($"kick \"{id}\" {Cmd.Quote(reason)}");
                break;
        }
    }
}
