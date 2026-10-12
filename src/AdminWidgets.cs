using System.Drawing;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>Helpers for building RCON commands from user text.</summary>
public static class Cmd
{
    /// <summary>Wraps user text in quotes for a console command, removing quotes and control characters so it cannot break out.</summary>
    public static string Quote(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Replace('"', '\'').Trim();
        if (clean.Length > 200) clean = clean[..200];
        return "\"" + clean + "\"";
    }
}

/// <summary>A button that lets the admin pick one of the players currently online.</summary>
public class PlayerPicker
{
    private readonly Core _core;
    private readonly string _placeholder;

    public Button Button { get; }
    public string? SteamId { get; private set; }
    public string? PlayerName { get; private set; }

    public PlayerPicker(Core core, string placeholder)
    {
        _core = core;
        _placeholder = placeholder;
        Button = Theme.GhostButton(placeholder + "   ▾");
        Button.Height = 38;
        Button.TextAlign = ContentAlignment.MiddleLeft;
        Button.Margin = new Padding(0, 4, 0, 0);
        Button.Click += (_, _) => ShowMenu();
    }

    private void ShowMenu()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        var players = _core.Monitor.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (players.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("No players online") { Enabled = false, BackColor = Theme.Input, ForeColor = Theme.Muted });
        foreach (var p in players)
        {
            var captured = p;
            var item = new ToolStripMenuItem($"{p.Name}   ({p.SteamId})") { Checked = p.SteamId == SteamId, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) => { SteamId = captured.SteamId; PlayerName = captured.Name; Button.Text = captured.Name + "   ▾"; };
            menu.Items.Add(item);
        }
        menu.Show(Button, new Point(0, Button.Height));
    }

    public void Reset() { SteamId = null; PlayerName = null; Button.Text = _placeholder + "   ▾"; }
}

/// <summary>Base for the admin tool widgets: a scrolling stack of controls that is disabled while not connected.</summary>
public abstract class ActionPanel : Panel
{
    protected readonly Core Core;
    private readonly TableLayoutPanel _stack;
    protected readonly Label Status = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, MaximumSize = new Size(420, 0), Margin = new Padding(0, 10, 0, 0) };

    protected ActionPanel(Core core)
    {
        Core = core;
        BackColor = Theme.Panel;
        AutoScroll = true;
        DarkScroll.Apply(this);
        _stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Theme.Panel,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows, Padding = new Padding(0, 4, 0, 0),
        };
        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_stack);
        Core.StateChanged += OnState;
    }

    protected void Add(Control c)
    {
        c.Dock = DockStyle.Top;
        if (c is ModernField) c.Margin = new Padding(0, c.Margin.Top, 0, c.Margin.Bottom); // line up with buttons and labels
        _stack.Controls.Add(c);
    }

    protected static Label Caption(string text) =>
        new() { Text = text, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };

    protected static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Margin = new Padding(0, 6, 0, 0) };
        foreach (var c in controls) { c.Margin = new Padding(0, 0, 8, 8); row.Controls.Add(c); }
        return row;
    }

    protected void Say(string text, Color color) { Status.Text = text; Status.ForeColor = color; }

    /// <summary>Call once the derived widget has built its controls.</summary>
    protected void Init() => OnState();

    /// <summary>How tall all the stacked controls want to be at the current width.</summary>
    protected int MeasureStack() => _stack.GetPreferredSize(new Size(Math.Max(ClientSize.Width, 100), 0)).Height;

    private void OnState() => Enable(Core.State == ConnState.Connected);

    protected abstract void Enable(bool connected);

    protected bool Confirm(string text, string title) =>
        MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    protected override void Dispose(bool disposing)
    {
        if (disposing) Core.StateChanged -= OnState;
        base.Dispose(disposing);
    }
}

/// <summary>Send a message to everyone in chat, with saved messages for reuse.</summary>
public class AnnounceView : ActionPanel
{
    private readonly ModernField _text = new();
    private readonly Button _send = Theme.PrimaryButton("Send to everyone");
    private readonly Button _saved = Theme.GhostButton("Saved ▾");

    public AnnounceView(Core core) : base(core)
    {
        _text.Box.PlaceholderText = "Message for all players";
        _text.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SendAsync(); } };
        _send.Height = _saved.Height = 36;
        _send.Click += async (_, _) => await SendAsync();
        _saved.Click += (_, _) => ShowSaved();

        Add(Caption("Message"));
        Add(_text);
        Add(Row(_send, _saved));
        Add(Status);
        Init();
    }

    protected override void Enable(bool connected)
    {
        _send.Enabled = connected;
        if (!connected) Say("Connect to a server to send messages.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private async Task SendAsync()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) { Say("Type a message first.", Theme.Err); return; }
        await Core.SendCommandAsync("say " + Cmd.Quote(text));
        Say("Sent to everyone.", Theme.Ok);
    }

    private void ShowSaved()
    {
        var list = Core.Settings.SavedAnnouncements;
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        foreach (var m in list.ToList())
        {
            var msg = m;
            var shown = msg.Length > 48 ? msg[..48] + "..." : msg;
            menu.Items.Add(new ToolStripMenuItem(shown, null, (_, _) => _text.Text = msg) { BackColor = Theme.Input, ForeColor = Theme.Text });
        }
        if (list.Count > 0) menu.Items.Add(new ToolStripSeparator());

        var save = new ToolStripMenuItem("Save this message") { BackColor = Theme.Input, ForeColor = Theme.Text };
        save.Click += (_, _) =>
        {
            var t = _text.Text.Trim();
            if (t.Length == 0 || list.Contains(t)) return;
            list.Add(t);
            Core.Settings.Save();
            Say("Message saved.", Theme.Ok);
        };
        menu.Items.Add(save);

        if (list.Count > 0)
        {
            var remove = new ToolStripMenuItem("Remove a saved message") { BackColor = Theme.Input, ForeColor = Theme.Muted };
            foreach (var m in list.ToList())
            {
                var msg = m;
                var shown = msg.Length > 40 ? msg[..40] + "..." : msg;
                remove.DropDownItems.Add(new ToolStripMenuItem(shown, null, (_, _) => { list.Remove(msg); Core.Settings.Save(); }) { BackColor = Theme.Input, ForeColor = Theme.Text });
            }
            remove.DropDown.BackColor = Theme.Input;
            menu.Items.Add(remove);
        }
        menu.Show(_saved, new Point(0, _saved.Height));
    }
}

/// <summary>One-click server actions: save, set the time of day, and shut down with a countdown.</summary>
public class QuickActionsView : ActionPanel
{
    private readonly Button _save = Theme.PrimaryButton("Save world");
    private readonly Button _time = Theme.GhostButton("Set time ▾");
    private readonly Button _restart = Theme.GhostButton("Restart ▾");

    public QuickActionsView(Core core) : base(core)
    {
        foreach (var b in new[] { _save, _time, _restart }) b.Height = 36;

        _save.Click += async (_, _) => { await Core.SendCommandAsync("server.save"); Say("World save requested.", Theme.Ok); };
        _time.Click += (_, _) =>
        {
            var menu = NewMenu();
            foreach (var (label, hour) in new[] { ("Morning (8:00)", 8), ("Noon (12:00)", 12), ("Evening (18:00)", 18), ("Midnight (0:00)", 0) })
            {
                var h = hour;
                menu.Items.Add(new ToolStripMenuItem(label, null, async (_, _) => { await Core.SendCommandAsync($"env.time {h}"); Say($"Time set to {label}.", Theme.Ok); }) { BackColor = Theme.Input, ForeColor = Theme.Text });
            }
            menu.Show(_time, new Point(0, _time.Height));
        };
        _restart.Click += (_, _) =>
        {
            var menu = NewMenu();
            foreach (var minutes in new[] { 1, 5, 10, 15, 30 })
            {
                var m = minutes;
                menu.Items.Add(new ToolStripMenuItem($"In {m} minute{(m == 1 ? "" : "s")}", null, async (_, _) =>
                {
                    // Rust's restart command warns players, then shuts the server down. Something outside the game
                    // (your host's auto-restart) has to start it again, and Rust has no built-in way to cancel it.
                    if (!Confirm($"Shut the server down in {m} minute{(m == 1 ? "" : "s")}?\n\nPlayers are warned first. Rust's restart command stops the server, so your host must start it again, and it cannot be cancelled from here.", "Restart server")) return;
                    await Core.SendCommandAsync($"restart {m * 60} \"Server restarting\"");
                    Say($"Restart in {m} minute{(m == 1 ? "" : "s")} requested.", Theme.Warn);
                }) { BackColor = Theme.Input, ForeColor = Theme.Text });
            }
            menu.Show(_restart, new Point(0, _restart.Height));
        };

        Add(Caption("Server"));
        Add(Row(_save, _time));
        Add(Caption("Shut down with a countdown"));
        Add(Row(_restart));
        Add(Status);
        Init();
    }

    private static ContextMenuStrip NewMenu() =>
        new() { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };

    protected override void Enable(bool connected)
    {
        _save.Enabled = _time.Enabled = _restart.Enabled = connected;
        if (!connected) Say("Connect to a server to use quick actions.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }
}

/// <summary>Kick, ban or mute the selected player, or unban a Steam ID.</summary>
public class ModerationView : ActionPanel
{
    private static readonly Regex SteamId = new(@"^\d{17}$", RegexOptions.Compiled);
    private readonly PlayerPicker _player;
    private readonly ModernField _reason = new();
    private readonly ModernField _unbanId = new();
    private readonly Button _kick = Theme.GhostButton("Kick");
    private readonly Button _ban = Theme.PrimaryButton("Ban");
    private readonly Button _mute = Theme.GhostButton("Mute");
    private readonly Button _unmute = Theme.GhostButton("Unmute");
    private readonly Button _unban = Theme.GhostButton("Unban");

    public ModerationView(Core core) : base(core)
    {
        _player = new PlayerPicker(core, "Select a player");
        _reason.Box.PlaceholderText = "Reason (shown to the player)";
        _unbanId.Box.PlaceholderText = "Steam ID to unban (17 digits)";
        foreach (var b in new[] { _kick, _ban, _mute, _unmute, _unban }) b.Height = 36;

        _kick.Click += async (_, _) => await ActAsync("kick");
        _ban.Click += async (_, _) => await ActAsync("ban");
        _mute.Click += async (_, _) => await MuteAsync(true);
        _unmute.Click += async (_, _) => await MuteAsync(false);
        _unban.Click += async (_, _) => await UnbanAsync();

        Add(_player.Button);
        Add(Caption("Reason"));
        Add(_reason);
        Add(Row(_kick, _ban, _mute, _unmute));
        Add(Caption("Unban"));
        Add(_unbanId);
        Add(Row(_unban));
        Add(Status);
        Init();
    }

    protected override void Enable(bool connected)
    {
        _kick.Enabled = _ban.Enabled = _mute.Enabled = _unmute.Enabled = _unban.Enabled = _player.Button.Enabled = connected;
        if (!connected) Say("Connect to a server to moderate players.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private async Task ActAsync(string verb)
    {
        if (_player.SteamId is null) { Say("Select a player first.", Theme.Err); return; }
        var reason = _reason.Text.Trim();
        if (reason.Length == 0) reason = verb == "ban" ? "Banned by admin" : "Kicked by admin";
        var name = _player.PlayerName ?? _player.SteamId;
        if (!Confirm($"{(verb == "ban" ? "Ban" : "Kick")} {name}?\n\nReason: {reason}", verb == "ban" ? "Ban player" : "Kick player")) return;

        if (verb == "ban")
        {
            await Core.SendCommandAsync($"banid {_player.SteamId} {Cmd.Quote(name)} {Cmd.Quote(reason)}");
            await Core.SendCommandAsync("server.writecfg"); // without this, bans can be lost when the server restarts
        }
        else await Core.SendCommandAsync($"kick \"{_player.SteamId}\" {Cmd.Quote(reason)}");
        Say($"{(verb == "ban" ? "Ban" : "Kick")} sent for {name}.", Theme.Ok);
        _player.Reset();
    }

    private async Task MuteAsync(bool mute)
    {
        if (_player.SteamId is null) { Say("Select a player first.", Theme.Err); return; }
        var name = _player.PlayerName ?? _player.SteamId;
        await Core.SendCommandAsync($"{(mute ? "mute" : "unmute")} \"{_player.SteamId}\"");
        Say($"{(mute ? "Mute" : "Unmute")} sent for {name}. Check the console for the server's reply.", Theme.Ok);
    }

    private async Task UnbanAsync()
    {
        var id = _unbanId.Text.Trim();
        if (!SteamId.IsMatch(id)) { Say("Enter a 17-digit Steam ID.", Theme.Err); return; }
        await Core.SendCommandAsync($"unban {id}"); // the ID goes without quotes
        await Core.SendCommandAsync("server.writecfg");
        Say($"Unban sent for {id}.", Theme.Ok);
    }
}

/// <summary>Move one online player to another online player.</summary>
public class TeleportView : ActionPanel
{
    private readonly PlayerPicker _who;
    private readonly PlayerPicker _to;
    private readonly Button _go = Theme.PrimaryButton("Teleport");

    public TeleportView(Core core) : base(core)
    {
        _who = new PlayerPicker(core, "Player to move");
        _to = new PlayerPicker(core, "Move them to player");
        _go.Height = 36;
        _go.Click += async (_, _) => await TeleportAsync();

        Add(_who.Button);
        Add(_to.Button);
        Add(Row(_go));
        Add(Status);
        Init();
    }

    protected override void Enable(bool connected)
    {
        _go.Enabled = _who.Button.Enabled = _to.Button.Enabled = connected;
        if (!connected) Say("Connect to a server to teleport players.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private async Task TeleportAsync()
    {
        if (_who.SteamId is null || _to.SteamId is null) { Say("Select both players.", Theme.Err); return; }
        if (_who.SteamId == _to.SteamId) { Say("Pick two different players.", Theme.Err); return; }
        await Core.SendCommandAsync($"teleport \"{_who.SteamId}\" \"{_to.SteamId}\"");
        Say($"Teleport sent: {_who.PlayerName} to {_to.PlayerName}. Check the console for the server's reply.", Theme.Ok);
    }
}

/// <summary>Your own buttons, each running a console command you choose.</summary>
public class ShortcutsView : ActionPanel
{
    private readonly FlowLayoutPanel _buttons = new() { AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _empty = new() { Text = "No shortcuts yet. Click \"+ Add shortcut\" to create a button for any console command.", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 8, 0, 0) };
    private readonly Button _add = Theme.GhostButton("+ Add shortcut");

    public ShortcutsView(Core core) : base(core)
    {
        _add.Height = 36;
        _add.Click += (_, _) => EditShortcut(null);
        Add(_buttons);
        Add(_empty);
        Add(Row(_add));
        Add(Status);
        Rebuild();
        Init();
    }

    protected override void Enable(bool connected)
    {
        foreach (var b in _buttons.Controls.OfType<Button>()) b.Enabled = connected;
        if (!connected && Status.Text.Length == 0) Say("Connect to a server to run shortcuts.", Theme.Muted);
        else if (connected && Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private void Rebuild()
    {
        foreach (var c in _buttons.Controls.Cast<Control>().ToList()) { _buttons.Controls.Remove(c); c.Dispose(); }
        foreach (var s in Core.Settings.Shortcuts)
        {
            var shortcut = s;
            var b = Theme.PrimaryButton(string.IsNullOrWhiteSpace(s.Label) ? s.Command : s.Label);
            b.Height = 36;
            b.Margin = new Padding(0, 0, 8, 8);
            b.Enabled = Core.State == ConnState.Connected;
            Tips.Set(b, s.Command + "\n(right-click to edit or remove)");
            b.Click += async (_, _) => await RunAsync(shortcut);
            var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem("Edit", null, (_, _) => EditShortcut(shortcut)) { BackColor = Theme.Input, ForeColor = Theme.Text });
            menu.Items.Add(new ToolStripMenuItem("Remove", null, (_, _) => { Core.Settings.Shortcuts.Remove(shortcut); Core.Settings.Save(); Rebuild(); }) { BackColor = Theme.Input, ForeColor = Theme.Text });
            b.ContextMenuStrip = menu;
            _buttons.Controls.Add(b);
        }
        _empty.Visible = Core.Settings.Shortcuts.Count == 0;
    }

    private async Task RunAsync(CommandShortcut s)
    {
        if (s.Confirm && !Confirm($"Run this command?\n\n{s.Command}", s.Label)) return;
        await Core.SendCommandAsync(s.Command);
        Say($"Ran: {s.Command}", Theme.Ok);
    }

    private void EditShortcut(CommandShortcut? existing)
    {
        using var f = new ShortcutForm(existing);
        if (f.ShowDialog(FindForm()) != DialogResult.OK || f.Result is not { } result) return;
        var list = Core.Settings.Shortcuts;
        if (existing is not null) { var i = list.IndexOf(existing); if (i >= 0) list[i] = result; else list.Add(result); }
        else list.Add(result);
        Core.Settings.Save();
        Rebuild();
        Init();
    }
}

/// <summary>Small dialog to create or edit a command shortcut.</summary>
public class ShortcutForm : Form
{
    private readonly ModernField _label = new();
    private readonly ModernField _command = new();
    private readonly CheckBox _confirm = new() { Text = "Ask before running", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand };
    public CommandShortcut? Result { get; private set; }

    public ShortcutForm(CommandShortcut? existing)
    {
        Theme.Apply(this);
        Text = existing is null ? "Add shortcut" : "Edit shortcut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 330);

        var ok = Theme.PrimaryButton("Save");
        var cancel = Theme.GhostButton("Cancel");
        var err = new Label { AutoSize = true, ForeColor = Theme.Err, Font = Theme.Small, MaximumSize = new Size(380, 0) };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 24, 28, 16) };
        flow.Controls.Add(Cap("Button label"));
        _label.Width = 380; flow.Controls.Add(_label);
        flow.Controls.Add(Cap("Console command"));
        _command.Width = 380; _command.Box.Font = Theme.Mono; flow.Controls.Add(_command);
        _confirm.Margin = new Padding(0, 16, 0, 0); flow.Controls.Add(_confirm);
        flow.Controls.Add(err);
        var bar = new FlowLayoutPanel { Width = 380, Height = 48, Margin = new Padding(0, 14, 0, 0), BackColor = Theme.Bg };
        bar.Controls.Add(ok); bar.Controls.Add(cancel);
        ok.Margin = new Padding(0, 0, 8, 0);
        flow.Controls.Add(bar);
        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });

        _label.Text = existing?.Label ?? "";
        _command.Text = existing?.Command ?? "";
        _confirm.Checked = existing?.Confirm ?? false;
        AcceptButton = ok;
        CancelButton = cancel;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        ok.Click += (_, _) =>
        {
            var command = _command.Text.Trim();
            if (command.Length == 0) { err.Text = "Enter the console command to run."; return; }
            Result = new CommandShortcut { Label = _label.Text.Trim(), Command = command, Confirm = _confirm.Checked };
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    private static Label Cap(string t) => new() { Text = t, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
}
