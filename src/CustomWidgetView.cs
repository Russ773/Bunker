using System.Drawing;
using System.Globalization;

namespace Bunker;

/// <summary>Draws a community widget from its JSON description. It can only send the commands listed on its buttons.</summary>
public class CustomWidgetView : ActionPanel
{
    private readonly WidgetDef _def;
    private readonly Dictionary<string, Func<string?>> _values = new();
    private readonly Dictionary<string, Func<string?>> _names = new();
    private readonly Dictionary<string, WidgetControl> _byId = new();
    private readonly Dictionary<string, IReplyTarget> _outputs = new();
    private readonly List<(PluginTableView View, string? PickField)> _picks = new();
    private readonly List<(Control Host, int MinHeight)> _outputHosts = new();
    private readonly List<(string Source, Label Value)> _stats = new();
    private readonly List<Button> _buttons = new();
    private CancellationTokenSource? _running;
    private readonly Dictionary<string, Action<string>> _setters = new();

    public CustomWidgetView(Core core, WidgetDef def) : base(core)
    {
        _def = def;
        FlowLayoutPanel? pendingRow = null;

        void Flush() { if (pendingRow is not null) { Add(pendingRow); pendingRow = null; } }

        foreach (var c in def.Controls)
        {
            if (c.Type != "button") Flush();
            if (c.Id is not null) _byId[c.Id] = c;

            switch (c.Type)
            {
                case "label":
                    Add(new Label { Text = c.Text ?? c.Label ?? "", ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 8, 0, 0) });
                    break;
                case "separator":
                    Add(new Panel { Height = 1, BackColor = Theme.Border, Margin = new Padding(0, 14, 0, 6) });
                    break;
                case "stat":
                    Add(BuildStat(c));
                    break;
                case "output":
                    Add(BuildOutput(c));
                    break;
                case "text":
                {
                    var f = new ModernField();
                    f.Box.PlaceholderText = c.Placeholder ?? "";
                    f.Box.MaxLength = 200;
                    Add(Caption(c.Label ?? c.Id ?? ""));
                    Add(f);
                    _values[c.Id!] = () => f.Text;
                    _setters[c.Id!] = v => f.Text = v;
                    break;
                }
                case "number":
                {
                    var f = new ModernField { Text = (c.Default ?? c.Min ?? 0).ToString(CultureInfo.InvariantCulture) };
                    f.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != '-') e.Handled = true; };
                    f.Box.MaxLength = 12;
                    Add(Caption(c.Label ?? c.Id ?? ""));
                    Add(f);
                    _values[c.Id!] = () => f.Text;
                    _setters[c.Id!] = v => f.Text = v;
                    break;
                }
                case "choice":
                {
                    var current = c.Options![0];
                    var button = Theme.GhostButton(current + "   ▾");
                    button.Height = 38;
                    button.TextAlign = ContentAlignment.MiddleLeft;
                    button.Margin = new Padding(0, 4, 0, 0);
                    var options = c.Options;
                    button.Click += (_, _) =>
                    {
                        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
                        foreach (var o in options)
                        {
                            var option = o;
                            var item = new ToolStripMenuItem(option) { Checked = option == current, BackColor = Theme.Input, ForeColor = Theme.Text };
                            item.Click += (_, _) => { current = option; button.Text = option + "   ▾"; };
                            menu.Items.Add(item);
                        }
                        menu.Show(button, new Point(0, button.Height));
                    };
                    Add(Caption(c.Label ?? c.Id ?? ""));
                    Add(button);
                    _values[c.Id!] = () => current;
                    break;
                }
                case "player":
                {
                    var picker = new PlayerPicker(core, c.Label ?? "Select a player");
                    Add(picker.Button);
                    _values[c.Id!] = () => picker.SteamId;
                    _names[c.Id!] = () => picker.PlayerName;
                    break;
                }
                case "button":
                {
                    var b = c.Style == "ghost" ? Theme.GhostButton(c.Text ?? "Run") : Theme.PrimaryButton(c.Text ?? "Run");
                    b.Height = 36;
                    b.Margin = new Padding(0, 0, 8, 8);
                    var control = c;
                    b.Click += async (_, _) => await RunAsync(control, b);
                    _buttons.Add(b);
                    pendingRow ??= new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Margin = new Padding(0, 10, 0, 0) };
                    pendingRow.Controls.Add(b);
                    break;
                }
            }
        }
        Flush();
        var textFields = def.Controls.Where(x => x.Type == "text" && x.Id is not null).Select(x => x.Id!).ToList();
        foreach (var (view, field) in _picks)
        {
            // With no "pick" given, a click on a plugin fills the widget's only text field.
            var target = field ?? (textFields.Count == 1 ? textFields[0] : null);
            if (target is not null && _setters.TryGetValue(target, out var set)) view.Picked = set;
        }
        // A single output takes all the room that is left, so the list is as long as the page allows and the buttons sit below it.
        Resize += (_, _) => FitOutput();
        VisibleChanged += (_, _) => FitOutput();
        Add(Status);
        if (_stats.Count > 0) { Core.Monitor.Updated += RefreshStats; RefreshStats(); }
#if DEBUG
        // Test hook (debug builds only): fill fields and press a button without a mouse.
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_PRESS") is { Length: > 0 } press)
        {
            var fill = Environment.GetEnvironmentVariable("BUNKER_TEST_FILL") ?? "";
            HandleCreated += async (_, _) =>
            {
                await Task.Delay(7500);
                foreach (var pair in fill.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    if (pair.Split('=', 2) is [var id, var value] && _setters.TryGetValue(id, out var set)) set(value);
                _buttons.FirstOrDefault(b => b.Text == press)?.PerformClick();
            };
        }
#endif
        Init();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Core.Monitor.Updated -= RefreshStats; _running?.Cancel(); }
        base.Dispose(disposing);
    }

    /// <summary>Stretches the single output box to the bottom of the page. Its own height is the least it will ever be.</summary>
    private void FitOutput()
    {
        if (_outputHosts.Count != 1 || !IsHandleCreated || ClientSize.Height < 100) return;
        var (host, min) = _outputHosts[0];
        var others = MeasureStack() - host.Height - host.Margin.Vertical;
        var wanted = Math.Max(min, ClientSize.Height - others - host.Margin.Vertical - 12);
        if (Math.Abs(wanted - host.Height) > 2) host.Height = wanted;
    }

    // ------------------------------------------------------------------ stats

    private Control BuildStat(WidgetControl c)
    {
        var tile = new Panel { Height = 70, BackColor = Theme.Input, Margin = new Padding(0, 10, 0, 0), Padding = new Padding(14, 10, 8, 6) };
        var value = new Label { Text = "-", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Size = new Size(540, 26), Location = new Point(12, 24) };
        tile.Controls.Add(value);
        tile.Controls.Add(new Label { Text = (c.Label ?? c.Source ?? "").ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(14, 8) });
        _stats.Add((c.Source ?? "", value));
        return tile;
    }

    private void RefreshStats()
    {
        var i = Core.Monitor.Info;
        foreach (var (source, label) in _stats)
        {
            label.Text = i is null ? "-" : source switch
            {
                "players" => $"{i.Players}",
                "maxplayers" => $"{i.MaxPlayers}",
                "fps" => i.Fps.ToString("0"),
                "entities" => i.Entities.ToString("N0"),
                "memory" => i.MemoryMb + " MB",
                "queued" => $"{i.Queued}",
                "joining" => $"{i.Joining}",
                "map" => string.IsNullOrEmpty(i.Map) ? "-" : i.Map,
                "uptime" => TimeSpan.FromSeconds(i.UptimeSeconds) is { } up ? (up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m") : "-",
                _ => "-",
            };
        }
    }

    // ------------------------------------------------------------------ output

    private Control BuildOutput(WidgetControl c)
    {
        // Every output can show a plugin list as a table (anything else is shown as text), so widgets written before the table existed
        // get it too. Only an output that asks for "text" is kept plain.
        IReplyTarget target;
        var minHeight = c.Height ?? 160;
        if (c.Format == "text") target = new TextReplyBox(Core, minHeight);
        else
        {
            var view = new PluginTableView(Core, Math.Max(minHeight, 300));
            _picks.Add((view, c.Pick));
            target = view;
        }
        _outputs[c.Id!] = target;
        _outputHosts.Add((target.Host, Math.Max(minHeight, c.Format == "text" ? minHeight : 300)));
        return target.Host;
    }

    private static readonly System.Text.RegularExpressions.Regex Markup =
        new(@"\x1B\[[0-9;]*[A-Za-z]|</?(?:color|size|b|i)(?:=[^>]*)?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Tidies a server reply for display: removes colour codes the console would have shown as colour, lines tabs up on 8-character
    /// stops (so a table keeps its columns), trims trailing spaces and squeezes runs of blank lines.
    /// </summary>
    public static string CleanReply(string reply)
    {
        var lines = new List<string>();
        var blank = 0;
        foreach (var raw in Markup.Replace(reply.Replace("\r\n", "\n").Replace('\r', '\n'), "").Split('\n'))
        {
            var line = ExpandTabs(raw).TrimEnd();
            if (line.Length == 0) { if (++blank > 1) continue; }
            else blank = 0;
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines).Trim('\r', '\n');
    }

    private static string ExpandTabs(string line)
    {
        if (!line.Contains('\t')) return line;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '\t') sb.Append(' ', 8 - sb.Length % 8);
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ running buttons

    protected override void Enable(bool connected)
    {
        foreach (var b in _buttons) b.Enabled = connected || _running is not null;
        if (!connected) Say("Connect to a server to use this widget.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private async Task RunAsync(WidgetControl button, Button visual)
    {
        // A second click on a running sequence stops it.
        if (_running is not null) { _running.Cancel(); return; }

        var commands = button.Commands ?? new List<string>();
        var values = new Dictionary<string, string>();
        var names = new Dictionary<string, string>();

        // Work out every value the commands need, checking each one before anything is sent.
        foreach (var id in commands.Concat(new[] { button.Confirm ?? "" }).SelectMany(WidgetRules.Placeholders).Distinct())
        {
            if (!_byId.TryGetValue(id, out var field) || !_values.TryGetValue(id, out var read)) continue;
            var label = field.Label ?? field.Id ?? id;
            var raw = read();
            if (string.IsNullOrWhiteSpace(raw)) { Say($"Fill in \"{label}\" first.", Theme.Err); return; }

            switch (field.Type)
            {
                case "number":
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { Say($"\"{label}\" must be a number.", Theme.Err); return; }
                    if (field.Min is { } min && number < min || field.Max is { } max && number > max)
                    { Say($"\"{label}\" must be between {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "any"} and {field.Max?.ToString(CultureInfo.InvariantCulture) ?? "any"}.", Theme.Err); return; }
                    values[id] = number.ToString(CultureInfo.InvariantCulture);
                    break;
                case "choice":
                    if (field.Options is null || !field.Options.Contains(raw)) { Say($"Pick a valid option for \"{label}\".", Theme.Err); return; }
                    values[id] = WidgetRules.Clean(raw);
                    break;
                case "player":
                    values[id] = new string(raw.Where(char.IsDigit).ToArray());
                    names[id] = WidgetRules.Clean(_names.TryGetValue(id, out var n) ? n() ?? raw : raw);
                    break;
                default:
                    values[id] = WidgetRules.Clean(raw);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(button.Confirm) &&
            !Confirm(WidgetRules.Expand(button.Confirm, values, names), _def.Name)) return;

        var output = button.Output is { } outId && _outputs.TryGetValue(outId, out var target) ? target : null;
        var originalText = visual.Text;
        var hasWaits = commands.Any(CommandRunner.TryWaitOnly);
        _running = new CancellationTokenSource();
        var token = _running.Token;
        if (hasWaits) visual.Text = "Stop";
        var sent = 0;
        try
        {
            for (var step = 0; step < commands.Count; step++)
            {
                token.ThrowIfCancellationRequested();
                var command = WidgetRules.Expand(commands[step], values, names).Trim();

                if (CommandRunner.TryWait(command, out var seconds))
                {
                    for (var left = seconds; left > 0; left--)
                    {
                        Say($"Waiting {left}s... (step {step + 1} of {commands.Count})", Theme.Warn);
                        await Task.Delay(1000, token);
                    }
                    continue;
                }
                if (Core.State != ConnState.Connected) { Say("Disconnected, so the rest was not sent.", Theme.Err); return; }

                if (output is not null)
                {
                    Core.Log.Add("> " + command, LogKind.Command);
                    output.AppendCommand(command);
                    string reply;
                    try { reply = await Core.Rcon.QueryAsync(command, 8000); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { reply = "(no reply from the server)"; }
                    output.AppendReply(reply);
                }
                else await Core.SendCommandAsync(command);
                sent++;
            }
            Say($"Sent {sent} command{(sent == 1 ? "" : "s")}." + (output is null ? " Check the console for the server's reply." : ""), Theme.Ok);
        }
        catch (OperationCanceledException) { Say("Stopped.", Theme.Warn); }
        finally
        {
            _running.Dispose();
            _running = null;
            visual.Text = originalText;
            Enable(Core.State == ConnState.Connected);
        }
    }
}
