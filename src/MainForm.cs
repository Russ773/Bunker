using System.Drawing;

namespace Bunker;

public class MainForm : Form
{
    private readonly Core _core = new(AppSettings.Load());
    private readonly Sidebar _sidebar;
    private readonly Dictionary<string, Control> _pages = new();

    public MainForm()
    {
        Theme.Apply(this);
        Text = "Bunker";
        Size = new Size(1280, 780);
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        if (_core.Settings.StartMaximised) WindowState = FormWindowState.Maximized;
        _sidebar = new Sidebar(_core);
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        _pages["dashboard"] = Pages.Dashboard(_core);
        _pages["console"] = Pages.Console(_core);
        _pages["players"] = Pages.Players(_core);
        _pages["layout"] = new LayoutPage(_core);
        _pages["tools"] = new ToolsPage(_core);
        _pages["settings"] = new SettingsPage(_core);
        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            content.Controls.Add(page);
        }

        Controls.Add(content);
        Controls.Add(_sidebar);

        _sidebar.Navigated += Navigate;
        _sidebar.ServerSwitchRequested += id => _ = _core.SwitchServerAsync(id);
        _sidebar.ConnectClicked += async () => await ToggleConnectionAsync();
        Navigate("dashboard");
#if DEBUG
        // Test hooks (debug builds only): open on a given page, and keep the window off-screen.
        if (Environment.GetEnvironmentVariable("BUNKER_START_PAGE") is { Length: > 0 } startPage && _pages.ContainsKey(startPage.Split(':')[0])) Navigate(startPage);
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") == "1") { WindowState = FormWindowState.Normal; StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); ShowInTaskbar = false; }
#endif

        Shown += async (_, _) => await StartupAsync();
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_REVIEW") is { Length: > 0 } reviewFile)
            Shown += async (_, _) => { await Task.Delay(4000); if (WidgetRules.Parse(File.ReadAllText(reviewFile), out _) is { } d) { using var f = new WidgetReviewForm(d, install: true); f.ShowDialog(this); } };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_FULLSCREEN") == "1")
            Shown += async (_, _) => { await Task.Delay(6000); using var f = new FullscreenForm(_core, this); f.ShowDialog(this); };
#endif
        FormClosing += (_, _) => _core.Rcon.Dispose();
    }

    private void Navigate(string key)
    {
        var section = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : null;
        if (section is not null) { key = key[..key.IndexOf(':')]; (_pages[key] as ISectionPage)?.ShowSection(section); }
        foreach (var (k, page) in _pages) page.Visible = k == key;
        _sidebar.Select(key);
    }

    private async Task StartupAsync()
    {
        if (await AutoUpdateAsync()) return;
        if (_core.Settings.ActiveServer is null) RunOnboarding();
        if (_core.Settings.ActiveServer is not null && _core.Settings.AutoConnect) await AutoConnectAsync();
    }

    private bool _autoConnectCancelled;

    /// <summary>Waits a few seconds after launch (the network and the window may still be settling), then connects, retrying a couple of times.</summary>
    private async Task AutoConnectAsync()
    {
        const int attempts = 3;
        _core.Log.Add("Connecting in 3 seconds...", LogKind.Info);
        await Task.Delay(3000);
        for (var i = 1; i <= attempts; i++)
        {
            if (_autoConnectCancelled || _core.State != ConnState.Disconnected) return;
            await _core.ConnectAsync();
            if (_core.State == ConnState.Connected || i == attempts) return;
            _core.Log.Add($"Retrying in 5 seconds (attempt {i + 1} of {attempts})...", LogKind.Info);
            await Task.Delay(5000);
        }
    }

    private bool RunOnboarding()
    {
        using var f = new OnboardingForm(null, firstRun: _core.Settings.ActiveServer is null);
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        _core.SaveServer(f.Result!, makeActive: true);
        return true;
    }

    private async Task ToggleConnectionAsync()
    {
        _autoConnectCancelled = true; // the user took over, so stop any automatic retries
        if (_core.State == ConnState.Connected) { await _core.DisconnectAsync(); return; }
        if (_core.Settings.ActiveServer is null && !RunOnboarding()) return;
        await _core.ConnectAsync();
    }

    /// <summary>Returns true if an update was started and the app is closing.</summary>
    private async Task<bool> AutoUpdateAsync()
    {
#if DEBUG
        return false; // dev builds never auto-update (they would close and replace the installed copy)
#endif
        if (!_core.Settings.CheckForUpdatesOnLaunch) return false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var update = await Updater.CheckAsync(cts.Token);
            if (update is null) return false;
            var answer = MessageBox.Show(this,
                $"Bunker {update.Version} is available (you have {Updater.CurrentVersion}).\n\nUpdate now? Bunker will close and restart.\nChoose No to keep using this version; you'll be asked again next time.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes)
            {
                _core.Log.Add($"Update {update.Version} is available (skipped for now). You can install it from Settings.", LogKind.Info);
                return false;
            }
            _core.Log.Add($"Downloading update {update.Version}...", LogKind.Warning);
            await Updater.DownloadAndLaunchAsync(update);
            Application.Exit();
            return true;
        }
        catch (Exception e)
        {
            _core.Log.Add("Update check failed: " + e.Message, LogKind.Info);
            return false;
        }
    }
}
