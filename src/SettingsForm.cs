using System.Drawing;

namespace Bunker;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly CheckBox _autoUpdate = new() { Text = "Check for updates when Bunker starts", AutoSize = true };
    private readonly Button _check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly ProgressBar _progress = new() { Width = 380, Visible = false };

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 220);
        BackColor = Color.FromArgb(29, 32, 38);
        ForeColor = Color.Gainsboro;

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = "Updates", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(new Label { Text = $"Current version: {Updater.CurrentVersion}", AutoSize = true, Margin = new Padding(3, 6, 3, 6) });
        _autoUpdate.Checked = settings.CheckForUpdatesOnLaunch;
        panel.Controls.Add(_autoUpdate);
        _check.Margin = new Padding(3, 10, 3, 6);
        panel.Controls.Add(_check);
        panel.Controls.Add(_progress);
        panel.Controls.Add(_result);
        Controls.Add(panel);

        _autoUpdate.CheckedChanged += (_, _) => { _settings.CheckForUpdatesOnLaunch = _autoUpdate.Checked; _settings.Save(); };
        _check.Click += async (_, _) => await CheckAsync();
    }

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _result.Text = "Checking...";
        try
        {
            var update = await Updater.CheckAsync();
            if (update is null) { _result.Text = "You're up to date."; return; }

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
            _result.Text = "Update check failed: " + e.Message;
        }
        finally
        {
            _check.Enabled = true;
        }
    }
}
