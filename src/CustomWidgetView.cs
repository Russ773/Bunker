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
    private readonly List<Button> _buttons = new();

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
                case "text":
                {
                    var f = new ModernField();
                    f.Box.PlaceholderText = c.Placeholder ?? "";
                    f.Box.MaxLength = 200;
                    Add(Caption(c.Label ?? c.Id ?? ""));
                    Add(f);
                    _values[c.Id!] = () => f.Text;
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
                    b.Click += async (_, _) => await RunAsync(control);
                    _buttons.Add(b);
                    pendingRow ??= new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Panel, Margin = new Padding(0, 10, 0, 0) };
                    pendingRow.Controls.Add(b);
                    break;
                }
            }
        }
        Flush();
        Add(Status);
        Init();
    }

    protected override void Enable(bool connected)
    {
        foreach (var b in _buttons) b.Enabled = connected;
        if (!connected) Say("Connect to a server to use this widget.", Theme.Muted);
        else if (Status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private async Task RunAsync(WidgetControl button)
    {
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

        foreach (var template in commands)
            await Core.SendCommandAsync(WidgetRules.Expand(template, values, names));
        Say($"Sent {commands.Count} command{(commands.Count == 1 ? "" : "s")}. Check the console for the server's reply.", Theme.Ok);
    }
}
