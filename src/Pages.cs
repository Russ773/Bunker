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
        stats.Dock = DockStyle.Fill;
        var statsHost = new Panel { Dock = DockStyle.Top, Height = 156, BackColor = Theme.Bg, Padding = new Padding(6, 0, 6, 0) };
        statsHost.Controls.Add(stats);

        body.Controls.Add(row);
        body.Controls.Add(statsHost);
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
        var p = new Panel { BackColor = Theme.Bg, Padding = new Padding(6) };
        card.Dock = DockStyle.Fill;
        p.Controls.Add(card);
        return p;
    }
}

/// <summary>The user's own view: pick a preset and choose what each section shows.</summary>
public class SettingsPage : Panel
{
    private readonly Core _core;
    private readonly Label _serverInfo = new() { AutoSize = false, AutoEllipsis = true, Size = new Size(500, 24), ForeColor = Theme.Text, Location = new Point(0, 6) };
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

        var startMax = new CheckBox
        {
            Text = "Start Bunker maximised", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand,
            Location = new Point(0, 6), Checked = core.Settings.StartMaximised,
        };
        startMax.CheckedChanged += (_, _) => { core.Settings.StartMaximised = startMax.Checked; core.Settings.Save(); };
        var generalBody = new Panel { BackColor = Theme.Panel };
        generalBody.Controls.Add(startMax);
        generalBody.Controls.Add(new Label { Text = "Takes effect the next time Bunker starts.", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 32) });
        var generalCard = Card.Titled("General", generalBody);
        generalCard.Size = new Size(560, 118);

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
        flow.Controls.Add(generalCard);
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
        Tips.Set(_serverInfo, _serverInfo.Text);
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
