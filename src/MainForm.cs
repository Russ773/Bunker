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

        _sidebar = new Sidebar(_core);
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        _pages["dashboard"] = Pages.Dashboard(_core);
        _pages["console"] = Pages.Console(_core);
        _pages["players"] = Pages.Players(_core);
        _pages["layout"] = new LayoutPage(_core);
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
        _sidebar.ConnectClicked += async () => await ToggleConnectionAsync();
        Navigate("dashboard");

        Shown += async (_, _) => await StartupAsync();
        FormClosing += (_, _) => _core.Rcon.Dispose();
    }

    private void Navigate(string key)
    {
        foreach (var (k, page) in _pages) page.Visible = k == key;
        _sidebar.Select(key);
    }

    private async Task StartupAsync()
    {
        if (await AutoUpdateAsync()) return;
        if (_core.Settings.Server is null) RunOnboarding();
        if (_core.Settings.Server is not null) await _core.ConnectAsync();
    }

    private bool RunOnboarding()
    {
        using var f = new OnboardingForm(_core.Settings.Server, firstRun: _core.Settings.Server is null);
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        _core.SetServer(f.Result);
        return true;
    }

    private async Task ToggleConnectionAsync()
    {
        if (_core.State == ConnState.Connected) { await _core.DisconnectAsync(); return; }
        if (_core.Settings.Server is null && !RunOnboarding()) return;
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
