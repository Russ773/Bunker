using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace Bunker;

/// <summary>What checking a widget for the shared library found.</summary>
public sealed record SubmissionCheck(bool Ok, WidgetDef? Def, List<string> Errors, List<(string Command, CommandRisk Risk)> Commands);

/// <summary>
/// Sharing a widget with everyone: the checks a widget has to pass, and the GitHub page that sends it in. A widget is sent as an issue on the
/// project's GitHub page, where it is checked again automatically and then read by a person before it joins the library.
/// </summary>
public static class WidgetSubmission
{
    public static string IssueBase => $"https://github.com/{Updater.Repo}/issues/new";

    /// <summary>GitHub refuses very long addresses. A widget that does not fit is put on the clipboard instead.</summary>
    public const int MaxUrlLength = 7000;

    /// <summary>The usual widget rules, plus what the shared library needs to tell widgets apart and credit the author.</summary>
    public static SubmissionCheck Check(string json)
    {
        var def = WidgetRules.Parse(json, out var errors);
        if (def is null) return new SubmissionCheck(false, null, errors, new());

        if (string.IsNullOrWhiteSpace(def.Author)) errors.Add("Add an \"author\" (your name or GitHub name) so people know who made it.");
        if (!Version.TryParse(def.Version, out _)) errors.Add("\"version\" must look like 1.0.0.");
        if (string.IsNullOrWhiteSpace(def.Description)) errors.Add("Add a \"description\" that says what the widget does.");

        var commands = WidgetRules.AllCommands(def).Distinct().Select(c => (c, WidgetRules.RiskOf(c))).ToList();
        return new SubmissionCheck(errors.Count == 0, def, errors, commands);
    }

    /// <summary>
    /// The address that opens a new widget submission on GitHub with the name, notes and the widget itself filled in. If the widget is too
    /// long for an address, it is left out (<paramref name="jsonIncluded"/> is false) and has to be pasted in.
    /// </summary>
    public static string IssueUrl(string json, WidgetDef def, string? notes, out bool jsonIncluded)
    {
        string Build(bool withJson)
        {
            var sb = new StringBuilder(IssueBase).Append("?template=widget-submission.yml");
            sb.Append("&title=").Append(Uri.EscapeDataString($"Widget: {def.Name} {def.Version}".Trim()));
            sb.Append("&widget_name=").Append(Uri.EscapeDataString(def.Name));
            if (!string.IsNullOrWhiteSpace(notes)) sb.Append("&widget_notes=").Append(Uri.EscapeDataString(notes.Trim()));
            if (withJson) sb.Append("&widget_json=").Append(Uri.EscapeDataString(json.Trim()));
            return sb.ToString();
        }

        var full = Build(true);
        jsonIncluded = full.Length <= MaxUrlLength;
        return jsonIncluded ? full : Build(false);
    }

    /// <summary>The result of a check as the JSON the automatic check on GitHub reads.</summary>
    public static string ResultJson(SubmissionCheck check) => JsonSerializer.Serialize(new
    {
        ok = check.Ok,
        id = check.Def?.Id,
        name = check.Def?.Name,
        author = check.Def?.Author,
        version = check.Def?.Version,
        description = check.Def?.Description,
        errors = check.Errors,
        commands = check.Commands.Select(c => new { command = c.Command, risk = c.Risk.ToString() }),
    }, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// <c>Bunker.exe --check-widget in.json out.json</c> checks a widget file without opening any window and writes the result as JSON.
/// The automatic check on GitHub uses it, so a submission is judged by exactly the rules the app applies.
/// </summary>
public static class WidgetCheck
{
    public static int Run(string inputPath, string outputPath)
    {
        SubmissionCheck check;
        try { check = WidgetSubmission.Check(File.ReadAllText(inputPath)); }
        catch (Exception e) { check = new SubmissionCheck(false, null, new List<string> { "Could not read the widget: " + e.Message }, new()); }
        File.WriteAllText(outputPath, WidgetSubmission.ResultJson(check));
        return check.Ok ? 0 : 1;
    }
}

/// <summary>Pick one of your widgets (or a file), see that it passes, add a note, and send it in through GitHub.</summary>
public sealed class ShareWidgetForm : Form
{
    private readonly Core _core;
    private readonly Label _picked = new() { AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(500, 0), Margin = new Padding(0, 8, 0, 0) };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 10, 0, 0) };
    private readonly TextBox _commands = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
        Font = Theme.Small, Width = 500, Height = 92, Margin = new Padding(0, 8, 0, 0), Visible = false,
    };
    private readonly ModernField _notes = new();
    private readonly Button _submit = Theme.PrimaryButton("Submit on GitHub");
    private string? _json;
    private SubmissionCheck? _check;

    public ShareWidgetForm(Core core)
    {
        _core = core;
        Theme.Apply(this);
        Text = "Share a widget";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 640);

        var choose = Theme.GhostButton("Choose a widget   ▾");
        choose.Width = 230;
        choose.Height = 36;
        choose.Click += (_, _) => ShowChooser(choose);
        _submit.Enabled = false;
        _submit.Height = 40;
        var cancel = Theme.GhostButton("Close");
        cancel.Height = 40;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(30, 24, 30, 10) };
        flow.Controls.Add(new Label { Text = "Share your widget with everyone", Font = Theme.Title, AutoSize = true, MaximumSize = new Size(500, 0) });
        flow.Controls.Add(new Label
        {
            Text = "Widgets you share appear in Browse library for every Bunker user, after they have been checked and read by a person. You need a free GitHub account: the widget goes in as a request on Bunker's GitHub page, with everything filled in for you.",
            ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 8, 0, 14),
        });
        flow.Controls.Add(choose);
        flow.Controls.Add(_picked);
        flow.Controls.Add(_result);
        flow.Controls.Add(_commands);
        flow.Controls.Add(new Label { Text = "Anything the reviewer should know? (optional)", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 16, 0, 4) });
        _notes.Width = 500;
        _notes.Box.PlaceholderText = "For example: tested on Carbon and Oxide";
        flow.Controls.Add(_notes);
        flow.Controls.Add(new Label
        {
            Text = "By submitting you agree that the widget can be shared under the project's MIT licence, and that you wrote it or have the right to share it.",
            ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 14, 0, 0),
        });

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg, Padding = new Padding(30, 14, 30, 14) };
        cancel.Dock = DockStyle.Right;
        bar.Controls.Add(cancel);
        bar.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        _submit.Dock = DockStyle.Right;
        _submit.Width = 190;
        bar.Controls.Add(_submit);

        Controls.Add(flow);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        CancelButton = cancel;
        cancel.Click += (_, _) => Close();
        _submit.Click += (_, _) => Submit();
        _picked.Text = "Pick one of the widgets you have installed, or a widget file from your PC.";
    }

    private void ShowChooser(Control anchor)
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        foreach (var def in _core.Widgets.All)
        {
            var id = def.Id;
            var item = new ToolStripMenuItem($"{def.Name}   (v{def.Version})") { BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) => { if (_core.Widgets.JsonOf(id) is { } json) Load(json, def.Name); };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        var file = new ToolStripMenuItem("A widget file on my PC...") { BackColor = Theme.Input, ForeColor = Theme.Text };
        file.Click += (_, _) => ChooseFile();
        menu.Items.Add(file);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a widget file", Filter = "Bunker widget (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > WidgetRules.MaxFileBytes) { Load("", dialog.FileName, tooBig: true); return; }
            Load(File.ReadAllText(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
        catch (Exception e) { Load("", dialog.FileName, readError: e.Message); }
    }

    internal void Load(string json, string source, bool tooBig = false, string? readError = null)
    {
        _json = json;
        _picked.Text = source;
        if (tooBig || readError is not null)
        {
            _check = null;
            Show(false, tooBig ? "That file is larger than 64 KB." : "Could not read the file: " + readError, "");
            return;
        }

        _check = WidgetSubmission.Check(json);
        if (!_check.Ok)
        {
            Show(false, "This widget can't be shared yet:\n- " + string.Join("\n- ", _check.Errors), "");
            return;
        }
        var def = _check.Def!;
        var risky = _check.Commands.Count(c => c.Risk != CommandRisk.Normal);
        Show(true, $"Ready to share: {def.Name} v{def.Version} by {def.Author}, {_check.Commands.Count} command{(_check.Commands.Count == 1 ? "" : "s")}" +
                   (risky > 0 ? $", {risky} marked ! for the reviewer" : "") + ".",
            string.Join(Environment.NewLine, _check.Commands.Select(c => (c.Risk == CommandRisk.Normal ? "   " : "!  ") + c.Command)));
    }

    private void Show(bool ok, string message, string commands)
    {
        _result.Text = message;
        _result.ForeColor = ok ? Theme.Ok : Theme.Err;
        _commands.Text = commands;
        _commands.Visible = commands.Length > 0;
        _submit.Enabled = ok;
    }

    private void Submit()
    {
        if (_check is not { Ok: true, Def: { } def } || _json is null) return;
        var url = WidgetSubmission.IssueUrl(_json, def, _notes.Text, out var included);
        try { Clipboard.SetText(_json); } catch { /* the clipboard can be busy; the address still carries the widget when it fits */ }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(this, "Could not open your browser: " + e.Message, "Share a widget", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        MessageBox.Show(this,
            included
                ? "GitHub is opening with your widget filled in. Check it, then press Submit new issue.\n\nA copy of the widget is also on your clipboard."
                : "GitHub is opening. This widget is too long to fill in by itself, so it is on your clipboard: click in the box called Widget file and press Ctrl+V, then press Submit new issue.",
            "Share a widget", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
