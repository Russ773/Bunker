using System.Drawing;

namespace Bunker;

public static class Pages
{
    public static Control Dashboard(Core core)
    {
        var body = new Panel { BackColor = Theme.Bg };

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        var players = new PlayersView(core);
        var playersCard = Card.Titled("Players", players, players.HeaderActions); playersCard.Dock = DockStyle.Fill;
        row.Controls.Add(playersCard, 0, 0);
        var console = new ConsoleView(core);
        var consoleCard = Card.Titled("Console", console, console.HeaderActions); consoleCard.Dock = DockStyle.Fill;
        row.Controls.Add(consoleCard, 1, 0);

        var stats = Card.Titled("Server", new ServerInfoView(core, 6));
        stats.Dock = DockStyle.Top;
        stats.Height = 150;

        body.Controls.Add(row);
        body.Controls.Add(stats);
        return new Page("Dashboard", "A live overview of your server", body);
    }

    public static Control Console(Core core)
    {
        var v = new ConsoleView(core);
        return new Page("Console", "Everything the server prints, and a box to run commands",
            WrapCard(Card.Titled("Console", v, v.HeaderActions)));
    }

    public static Control Players(Core core)
    {
        var v = new PlayersView(core);
        return new Page("Players", "Who is on the server right now. Right-click a player to copy their ID, kick or ban",
            WrapCard(Card.Titled("Players", v, v.HeaderActions)));
    }

    private static Control WrapCard(Card card)
    {
        var p = new Panel { BackColor = Theme.Bg };
        card.Dock = DockStyle.Fill;
        p.Controls.Add(card);
        return p;
    }
}

/// <summary>The user's own view: pick a preset and choose what each section shows.</summary>
public class LayoutPage : Panel
{
    private readonly Core _core;
    private readonly Panel _host = new() { BackColor = Theme.Bg };
    private readonly List<PresetButton> _buttons = new();

    public LayoutPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;
        foreach (var preset in Enum.GetValues<LayoutPreset>())
        {
            var b = new PresetButton(preset);
            b.Click += (_, _) => { _core.Settings.Layout.Preset = preset; _core.Settings.Save(); Rebuild(); };
            _buttons.Add(b);
        }
        var page = new Page("Layout", "Choose how many sections to show, then pick what goes in each one",
            _host, _buttons.ToArray());
        page.Dock = DockStyle.Fill;
        Controls.Add(page);
        Rebuild();
    }

    private void Rebuild()
    {
        var layout = _core.Settings.Layout;
        if (layout.Slots is not { Length: >= 4 })
        {
            var fixedSlots = new WidgetKind[4];
            for (var i = 0; i < 4; i++) fixedSlots[i] = layout.SlotAt(i);
            layout.Slots = fixedSlots;
        }

        foreach (var b in _buttons) { b.Selected = b.Preset == layout.Preset; b.Invalidate(); }

        _host.SuspendLayout();
        foreach (var c in _host.Controls.Cast<Control>().ToList()) { _host.Controls.Remove(c); c.Dispose(); }

        var preset = layout.Preset;
        var cols = preset is LayoutPreset.Single or LayoutPreset.TwoRows ? 1 : 2;
        var rows = preset is LayoutPreset.Single or LayoutPreset.TwoColumns ? 1 : 2;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, BackColor = Theme.Bg };
        for (var c = 0; c < cols; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        for (var r = 0; r < rows; r++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        var cells = PresetButton.Cells(preset);
        for (var i = 0; i < cells.Length; i++)
        {
            var (x, y, w, h) = cells[i];
            var card = MakeSlot(i, w >= 1f ? 6 : 2);
            card.Dock = DockStyle.Fill;
            table.Controls.Add(card, (int)Math.Round(x * cols), (int)Math.Round(y * rows));
            table.SetColumnSpan(card, Math.Max(1, (int)Math.Round(w * cols)));
            table.SetRowSpan(card, Math.Max(1, (int)Math.Round(h * rows)));
        }

        _host.Controls.Add(table);
        _host.ResumeLayout();
    }

    private Card MakeSlot(int index, int infoColumns)
    {
        var kind = _core.Settings.Layout.SlotAt(index);
        var picker = Theme.GhostButton(Widgets.Name(kind) + "   ▾");
        picker.Width = 160;
        picker.Height = 30;
        picker.Location = new Point(0, 6);
        picker.Font = Theme.Semibold;

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        foreach (var k in Widgets.All)
        {
            var item = new ToolStripMenuItem(Widgets.Name(k)) { Checked = k == kind, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) =>
            {
                _core.Settings.Layout.Slots[index] = k;
                _core.Settings.Save();
                BeginInvoke(Rebuild);
            };
            menu.Items.Add(item);
        }
        picker.Click += (_, _) => menu.Show(picker, new Point(0, picker.Height));
        var holder = new Panel { Width = 170, BackColor = Theme.Panel };
        holder.Controls.Add(picker);

        var (content, actions) = Widgets.Create(kind, _core, infoColumns);
        return new Card(holder, content, actions);
    }
}

public class SettingsPage : Panel
{
    private readonly Core _core;
    private readonly Label _serverInfo = new() { AutoSize = true, ForeColor = Theme.Text, Location = new Point(0, 6) };
    private readonly Button _editServer = Theme.GhostButton("Edit server");
    private readonly CheckBox _autoUpdate = new() { Text = "Check for updates when Bunker starts (you are asked before anything installs)", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(0, 40) };
    private readonly Button _check = Theme.PrimaryButton("Check for updates");
    private readonly ProgressBar _progress = new() { Width = 420, Height = 6, Visible = false, Location = new Point(0, 118) };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Theme.Muted, Location = new Point(0, 132) };

    public SettingsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        var serverBody = new Panel { BackColor = Theme.Panel };
        _editServer.Location = new Point(0, 42);
        serverBody.Controls.Add(_serverInfo);
        serverBody.Controls.Add(_editServer);
        var serverCard = Card.Titled("Server", serverBody);
        serverCard.Size = new Size(560, 150);

        var updateBody = new Panel { BackColor = Theme.Panel };
        updateBody.Controls.Add(new Label { Text = $"Current version: {Updater.VersionText}", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(0, 6) });
        _autoUpdate.Location = new Point(0, 36);
        _check.Location = new Point(0, 68);
        updateBody.Controls.Add(_autoUpdate);
        updateBody.Controls.Add(_check);
        updateBody.Controls.Add(_progress);
        updateBody.Controls.Add(_result);
        var updateCard = Card.Titled("Updates", updateBody);
        updateCard.Size = new Size(560, 230);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        flow.Controls.Add(serverCard);
        flow.Controls.Add(updateCard);

        var page = new Page("Settings", "Your server connection and app updates", flow);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        _autoUpdate.Checked = core.Settings.CheckForUpdatesOnLaunch;
        _autoUpdate.CheckedChanged += (_, _) => { core.Settings.CheckForUpdatesOnLaunch = _autoUpdate.Checked; core.Settings.Save(); };
        _editServer.Click += (_, _) =>
        {
            using var f = new OnboardingForm(core.Settings.Server, firstRun: core.Settings.Server is null);
            if (f.ShowDialog(FindForm()) == DialogResult.OK) core.SetServer(f.Result);
        };
        _check.Click += async (_, _) => await CheckAsync();
        core.ServerChanged += RefreshServer;
        RefreshServer();
    }

    private void RefreshServer()
    {
        var s = _core.Settings.Server;
        _serverInfo.Text = s is null ? "No server configured yet." : $"{s.DisplayName}   -   {s.Host}:{s.Port}";
        _editServer.Text = s is null ? "Add server" : "Edit server";
    }

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _result.ForeColor = Theme.Muted;
        _result.Text = "Checking...";
        try
        {
            var update = await Updater.CheckAsync();
            if (update is null) { _result.ForeColor = Theme.Ok; _result.Text = "You're up to date."; return; }

            var answer = MessageBox.Show(FindForm(),
                $"Version {update.Version} is available (you have {Updater.CurrentVersion}).\n\nInstall now? Bunker will restart.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) { _result.Text = $"Version {update.Version} is available."; return; }

            _progress.Visible = true;
            _result.Text = "Downloading...";
            await Updater.DownloadAndLaunchAsync(update, new Progress<int>(p => _progress.Value = p));
            Application.Exit();
        }
        catch (Exception e)
        {
            _result.ForeColor = Theme.Err;
            _result.Text = "Update check failed: " + e.Message;
        }
        finally { _check.Enabled = true; }
    }
}
