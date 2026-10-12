using System.Drawing;

namespace Bunker;

/// <summary>Create, edit and run schedules: recurring announcements, daily saves, restart warnings.</summary>
public class SchedulerView : Panel
{
    private readonly Core _core;
    private readonly DarkGrid _grid = new();
    private readonly Button _add = Theme.PrimaryButton("Add schedule");
    private readonly Button _edit = Theme.GhostButton("Edit");
    private readonly Button _toggle = Theme.GhostButton("Turn off");
    private readonly Button _runNow = Theme.GhostButton("Run now");
    private readonly Button _remove = Theme.GhostButton("Remove");
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };

    public SchedulerView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        var hint = new Label
        {
            Text = "Schedules run on their own while Bunker is open and connected: for example an announcement every hour, a world save at 04:00, or a restart warning sequence. Runs missed while Bunker was closed are not caught up.",
            Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0),
        };

        _grid.Columns.Add("On", "On");
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("When", "Runs");
        _grid.Columns.Add("Next", "Next run");
        _grid.Columns.Add("Last", "Last run");
        _grid.Columns.Add("Steps", "Commands");
        _grid.Columns[0].FillWeight = 22;
        _grid.Columns[1].FillWeight = 120;
        _grid.Columns[2].FillWeight = 100;
        _grid.Columns[3].FillWeight = 80;
        _grid.Columns[4].FillWeight = 80;
        _grid.Columns[5].FillWeight = 56;
        _grid.Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Panel };
        foreach (var b in new[] { _add, _edit, _toggle, _runNow, _remove }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); bar.Controls.Add(b); }

        Controls.Add(_grid);
        Controls.Add(bar);
        Controls.Add(hint);

        _add.Click += (_, _) => Edit(null);
        _edit.Click += (_, _) => Edit(Selected());
        _toggle.Click += (_, _) => { if (Selected() is { } t) { t.Enabled = !t.Enabled; _core.Settings.Save(); Fill(); } };
        _runNow.Click += async (_, _) => { if (Selected() is { } t) await _core.Scheduler.RunAsync(t); };
        _remove.Click += (_, _) => Remove();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Edit(Selected()); };
        _core.Scheduler.Changed += Fill;
        _core.StateChanged += UpdateButtons;
        _clock.Tick += (_, _) => RefreshTimes();
        _clock.Start();
        Fill();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _core.Scheduler.Changed -= Fill; _core.StateChanged -= UpdateButtons; _clock.Dispose(); }
        base.Dispose(disposing);
    }

    private ScheduledTask? Selected() => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ScheduledTask : null;

    private void UpdateButtons()
    {
        var t = Selected();
        _edit.Enabled = _remove.Enabled = _toggle.Enabled = t is not null;
        _runNow.Enabled = t is not null && _core.State == ConnState.Connected && !_core.Scheduler.IsRunning(t.Id);
        _toggle.Text = t is { Enabled: false } ? "Turn on" : "Turn off";
    }

    private static string WhenText(ScheduledTask t) =>
        t.Mode == "daily" ? $"Every day at {t.TimeOfDay}" : t.IntervalMinutes == 60 ? "Every hour" : $"Every {t.IntervalMinutes} minutes";

    private string NextText(ScheduledTask t)
    {
        if (_core.Scheduler.IsRunning(t.Id)) return "Running now...";
        if (_core.Scheduler.NextRun(t) is not { } next) return t.Enabled ? "-" : "Off";
        if (_core.State != ConnState.Connected) return "When connected";
        var left = next - DateTime.Now;
        if (left.TotalSeconds < 0) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{next:ddd HH:mm}  (in {(int)left.TotalHours}h {left.Minutes}m)" : $"in {left.Minutes}m {left.Seconds:00}s";
    }

    private static string LastText(ScheduledTask t) =>
        t.LastRunUtc is { } last ? last.ToLocalTime().ToString("ddd HH:mm") : "Never";

    private void Fill()
    {
        var selectedId = Selected()?.Id;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var t in _core.Settings.Schedules)
        {
            var i = _grid.Rows.Add(t.Enabled ? "●" : "○", t.Name, WhenText(t), NextText(t), LastText(t), t.Commands.Count(c => !string.IsNullOrWhiteSpace(c)));
            _grid.Rows[i].Tag = t;
            _grid.Rows[i].Cells[0].Style.ForeColor = t.Enabled ? Theme.Ok : Theme.Muted;
            if (t.Id == selectedId) _grid.Rows[i].Selected = true;
        }
        _grid.ResumeLayout();
        if (_grid.Rows.Count > 0 && _grid.SelectedRows.Count == 0) _grid.Rows[0].Selected = true;
        UpdateButtons();
    }

    /// <summary>Keeps the "in 12m 04s" countdowns ticking without rebuilding the table.</summary>
    private void RefreshTimes()
    {
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is ScheduledTask t) row.Cells[3].Value = NextText(t);
    }

    private void Edit(ScheduledTask? existing)
    {
        using var f = new ScheduleForm(existing);
        if (f.ShowDialog(FindForm()) != DialogResult.OK || f.Result is not { } result) return;
        var list = _core.Settings.Schedules;
        if (existing is not null)
        {
            var i = list.IndexOf(existing);
            result.Id = existing.Id;
            result.LastRunUtc = existing.LastRunUtc;
            if (i >= 0) list[i] = result; else list.Add(result);
        }
        else list.Add(result);
        _core.Settings.Save();
        Fill();
    }

    private void Remove()
    {
        if (Selected() is not { } t) return;
        var ok = MessageBox.Show(FindForm(), $"Remove the schedule \"{t.Name}\"?", "Remove schedule", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;
        _core.Settings.Schedules.Remove(t);
        _core.Settings.Save();
        Fill();
    }
}

/// <summary>Create or edit one schedule.</summary>
public class ScheduleForm : Form
{
    private readonly ModernField _name = new();
    private readonly TextBox _commands = new()
    {
        Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = Theme.Mono, BackColor = Theme.Input,
        ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Height = 150, Width = 480, WordWrap = false,
    };
    private readonly RadioButton _interval = new() { Text = "Every", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand };
    private readonly RadioButton _daily = new() { Text = "Every day at", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand };
    private readonly ModernField _minutes = new();
    private readonly ModernField _time = new();
    private readonly Label _error = new() { AutoSize = true, ForeColor = Theme.Err, Font = Theme.Small, MaximumSize = new Size(480, 0) };

    public ScheduledTask? Result { get; private set; }

    public ScheduleForm(ScheduledTask? existing)
    {
        Theme.Apply(this);
        Text = existing is null ? "Add schedule" : "Edit schedule";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 640);

        var ok = Theme.PrimaryButton("Save");
        var cancel = Theme.GhostButton("Cancel");
        var examples = Theme.GhostButton("Examples ▾");
        DarkScroll.Apply(_commands);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 22, 28, 14) };
        flow.Controls.Add(Cap("Name"));
        _name.Width = 480; _name.Box.PlaceholderText = "Hourly reminder"; flow.Controls.Add(_name);

        flow.Controls.Add(Cap("When"));
        var when = new FlowLayoutPanel { Width = 480, Height = 50, BackColor = Theme.Bg, WrapContents = false };
        _minutes.Width = 80; _minutes.Margin = new Padding(0, 0, 8, 0);
        _time.Width = 90; _time.Margin = new Padding(0, 0, 8, 0);
        var minutesText = new Label { Text = "minutes", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 10, 28, 0) };
        _interval.Margin = new Padding(0, 10, 10, 0);
        _daily.Margin = new Padding(0, 10, 10, 0);
        when.Controls.AddRange(new Control[] { _interval, _minutes, minutesText, _daily, _time });
        flow.Controls.Add(when);

        flow.Controls.Add(Cap("Commands, one per line"));
        flow.Controls.Add(_commands);
        flow.Controls.Add(new Label
        {
            Text = "Tip: a line like  wait 30  pauses for 30 seconds before the next command. Use it to build countdowns.",
            ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 8, 0, 0),
        });
        flow.Controls.Add(_error);

        var bar = new FlowLayoutPanel { Width = 480, Height = 52, Margin = new Padding(0, 14, 0, 0), BackColor = Theme.Bg };
        ok.Margin = new Padding(0, 0, 8, 0);
        cancel.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(ok); bar.Controls.Add(cancel); bar.Controls.Add(examples);
        flow.Controls.Add(bar);

        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });

        _name.Text = existing?.Name ?? "";
        _minutes.Text = (existing?.IntervalMinutes ?? 60).ToString();
        _time.Text = existing?.TimeOfDay ?? "04:00";
        _commands.Text = string.Join(Environment.NewLine, existing?.Commands ?? new List<string> { "say \"Remember to read the rules!\"" });
        (existing?.Mode == "daily" ? _daily : _interval).Checked = true;
        _minutes.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

        examples.Click += (_, _) => ShowExamples(examples);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        ok.Click += (_, _) => Save(existing);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private static Label Cap(string t) => new() { Text = t, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };

    private void ShowExamples(Control anchor)
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        void Example(string label, string name, string mode, int minutes, string time, params string[] lines)
        {
            var item = new ToolStripMenuItem(label) { BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) =>
            {
                _name.Text = name;
                (mode == "daily" ? _daily : _interval).Checked = true;
                _minutes.Text = minutes.ToString();
                _time.Text = time;
                _commands.Text = string.Join(Environment.NewLine, lines);
            };
            menu.Items.Add(item);
        }
        Example("Hourly reminder", "Hourly reminder", "interval", 60, "04:00", "say \"Remember to read the rules, and have fun!\"");
        Example("Daily world save at 04:00", "Daily save", "daily", 60, "04:00", "server.save");
        Example("Restart warnings (run before a restart)", "Restart warnings", "daily", 60, "05:00",
            "say \"Server restart in 5 minutes!\"", "wait 120", "say \"Server restart in 3 minutes!\"", "wait 120", "say \"Server restart in 1 minute! Finish up!\"", "wait 50", "say \"Restarting in 10 seconds...\"", "wait 10", "server.save");
        menu.Show(anchor, new Point(0, -menu.PreferredSize.Height));
    }

    private void Save(ScheduledTask? existing)
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) { _error.Text = "Give the schedule a name."; return; }
        var lines = _commands.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || lines.All(CommandRunner.TryWaitOnly)) { _error.Text = "Add at least one command."; return; }
        if (lines.Count > 30 || lines.Any(l => l.Length > 300)) { _error.Text = "Use at most 30 lines of up to 300 characters."; return; }

        var daily = _daily.Checked;
        var minutes = 60;
        if (!daily && (!int.TryParse(_minutes.Text, out minutes) || minutes is < 1 or > 10080)) { _error.Text = "Enter a number of minutes from 1 to 10080."; return; }
        var timeOfDay = TimeSpan.Zero;
        if (daily && !Scheduler.TryParseTime(_time.Text, out timeOfDay)) { _error.Text = "Enter the time as hours:minutes, for example 04:00."; return; }

        Result = new ScheduledTask
        {
            Name = name, Commands = lines, Mode = daily ? "daily" : "interval", IntervalMinutes = minutes,
            TimeOfDay = timeOfDay.ToString(@"hh\:mm"), Enabled = existing?.Enabled ?? true,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
