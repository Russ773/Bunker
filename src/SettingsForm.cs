using System.Drawing;

namespace Bunker;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly CheckBox _autoUpdate = new() { Text = "Check for updates when Bunker starts", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand };
    private readonly Button _check = Theme.PrimaryButton("Check for updates");
    private readonly Button _editServer = Theme.GhostButton("Edit server");
    private readonly Label _serverInfo = new() { AutoSize = true, ForeColor = Theme.Muted };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 10, 0, 0) };
    private readonly ProgressBar _progress = new() { Width = 380, Height = 6, Visible = false, Margin = new Padding(0, 12, 0, 0) };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Theme.Apply(this);
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 380);

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 24, 28, 16) };
        panel.Controls.Add(Heading("Server"));
        panel.Controls.Add(_serverInfo);
        _editServer.Margin = new Padding(0, 10, 0, 0);
        panel.Controls.Add(_editServer);

        panel.Controls.Add(Heading("Updates", 26));
        panel.Controls.Add(new Label { Text = $"Current version: {Updater.CurrentVersion}", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 8) });
        _autoUpdate.Checked = settings.CheckForUpdatesOnLaunch;
        panel.Controls.Add(_autoUpdate);
        _check.Margin = new Padding(0, 12, 0, 0);
        panel.Controls.Add(_check);
        panel.Controls.Add(_progress);
        panel.Controls.Add(_result);
        Controls.Add(panel);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });

        RefreshServer();
        _autoUpdate.CheckedChanged += (_, _) => { _settings.CheckForUpdatesOnLaunch = _autoUpdate.Checked; _settings.Save(); };
        _check.Click += async (_, _) => await CheckAsync();
        _editServer.Click += (_, _) =>
        {
            using var f = new OnboardingForm(_settings.Server, firstRun: _settings.Server is null);
            if (f.ShowDialog(this) == DialogResult.OK) { _settings.Server = f.Result; _settings.Save(); RefreshServer(); }
        };
    }

    private static Label Heading(string text, int top = 0) =>
        new() { Text = text, Font = Theme.Semibold, AutoSize = true, Margin = new Padding(0, top, 0, 8) };

    private void RefreshServer()
    {
        var s = _settings.Server;
        _serverInfo.Text = s is null ? "No server configured yet." : $"{s.DisplayName}  -  {s.Host}:{s.Port}";
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

            var answer = MessageBox.Show(this,
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
        finally
        {
            _check.Enabled = true;
        }
    }
}
