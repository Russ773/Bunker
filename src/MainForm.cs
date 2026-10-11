using System.Drawing;

namespace Bunker;

public class MainForm : Form
{
    private const int MaxChars = 500_000;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly RconClient _rcon = new();
    private readonly List<string> _history = new();
    private int _historyIndex;

    private readonly Label _serverName = new() { Font = Theme.Semibold, AutoSize = true };
    private readonly Label _serverAddr = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 9, 14, 0) };
    private readonly Button _connect = Theme.PrimaryButton("Connect");
    private readonly Button _disconnect = Theme.GhostButton("Disconnect");
    private readonly Button _clear = Theme.GhostButton("Clear");
    private readonly Button _settingsBtn = Theme.GhostButton("Settings");
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 14, 0), Cursor = Cursors.Hand };
    private readonly RichTextBox _console = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Mono, HideSelection = false, DetectUrls = false,
    };
    private readonly ModernField _input = new();
    private readonly Button _send = Theme.PrimaryButton("Send");

    public MainForm()
    {
        Theme.Apply(this);
        Text = "Bunker";
        Size = new Size(1100, 700);
        MinimumSize = new Size(820, 440);

        // Header
        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Theme.Panel };
        header.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
        var brand = new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(22, 20) };
        _serverName.Location = new Point(140, 14);
        _serverAddr.Location = new Point(140, 36);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 15, 18, 0), BackColor = Color.Transparent };
        foreach (var b in new[] { _connect, _disconnect, _clear, _settingsBtn }) b.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.AddRange(new Control[] { _status, _autoScroll, _clear, _settingsBtn, _disconnect, _connect });
        header.Controls.AddRange(new Control[] { brand, _serverName, _serverAddr, actions });

        // Command bar
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Theme.Panel, Padding = new Padding(16, 14, 16, 14) };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        _input.Dock = DockStyle.Fill;
        _input.Box.Font = Theme.Mono;
        _input.Box.PlaceholderText = "Type a command (e.g. status, say hello)  -  Up/Down for history";
        var gap = new Panel { Dock = DockStyle.Right, Width = 10 };
        _send.Dock = DockStyle.Right;
        _send.Width = 96;
        bar.Controls.AddRange(new Control[] { _input, gap, _send });

        var consoleHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(20, 14, 12, 8) };
        consoleHost.Controls.Add(_console);

        Controls.Add(consoleHost);
        Controls.Add(header);
        Controls.Add(bar);
        AcceptButton = _send;

        RefreshServer();
        SetConnected(false, "Disconnected");

        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += async (_, _) => { await _rcon.DisconnectAsync(); SetConnected(false, "Disconnected"); };
        _clear.Click += (_, _) => _console.Clear();
        _send.Click += async (_, _) => await SendAsync();
        _input.Box.KeyDown += InputKeyDown;
        _settingsBtn.Click += (_, _) => { using var f = new SettingsForm(_settings); f.ShowDialog(this); RefreshServer(); };
        _rcon.MessageReceived += m => BeginInvoke(() => OnMessage(m));
        _rcon.Disconnected += reason => BeginInvoke(() => { Append(reason, Theme.Muted); SetConnected(false, reason); });
        Shown += async (_, _) => await StartupAsync();
        FormClosing += (_, _) => _rcon.Dispose();
    }

    private async Task StartupAsync()
    {
        if (await AutoUpdateAsync()) return;
        if (_settings.Server is null) RunOnboarding();
        if (_settings.Server is not null) await ConnectAsync();
    }

    private bool RunOnboarding()
    {
        using var f = new OnboardingForm(_settings.Server, firstRun: _settings.Server is null);
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        _settings.Server = f.Result;
        _settings.Save();
        RefreshServer();
        return true;
    }

    private void RefreshServer()
    {
        var s = _settings.Server;
        _serverName.Text = s?.DisplayName ?? "No server yet";
        _serverAddr.Text = s is null ? "Click Connect to add one" : $"{s.Host}:{s.Port}";
    }

    /// <summary>Returns true if an update was started and the app is closing.</summary>
    private async Task<bool> AutoUpdateAsync()
    {
        if (!_settings.CheckForUpdatesOnLaunch) return false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var update = await Updater.CheckAsync(cts.Token);
            if (update is null) return false;
            Append($"Update {update.Version} found - downloading and installing...", Theme.Warn);
            SetStatusText("Updating...", Theme.Warn);
            await Updater.DownloadAndLaunchAsync(update);
            Application.Exit();
            return true;
        }
        catch (Exception e)
        {
            Append("Update check failed: " + e.Message, Theme.Muted);
            return false;
        }
    }

    private async Task ConnectAsync()
    {
        if (_settings.Server is null && !RunOnboarding()) return;
        var s = _settings.Server!;
        _connect.Enabled = false;
        SetStatusText("Connecting...", Theme.Warn);
        try
        {
            await _rcon.ConnectAsync(s.Host, s.Port, s.Password);
            Append($"Connected to {s.DisplayName} ({s.Host}:{s.Port})", Theme.Muted);
            SetConnected(true, "Connected");
            await _rcon.SendCommandAsync("serverinfo");
        }
        catch (Exception e)
        {
            Append("Connection failed: " + e.Message, Theme.Err);
            SetConnected(false, "Connection failed");
        }
    }

    private async Task SendAsync()
    {
        var cmd = _input.Text.Trim();
        if (cmd.Length == 0 || !_rcon.IsConnected) return;
        Append("> " + cmd, Theme.Muted);
        _history.Add(cmd);
        _historyIndex = _history.Count;
        _input.Text = "";
        try { await _rcon.SendCommandAsync(cmd); }
        catch (Exception e) { Append("Send failed: " + e.Message, Theme.Err); }
    }

    private void InputKeyDown(object? s, KeyEventArgs e)
    {
        var box = _input.Box;
        if (e.KeyCode == Keys.Up && _historyIndex > 0) { box.Text = _history[--_historyIndex]; box.SelectionStart = box.TextLength; e.Handled = true; }
        else if (e.KeyCode == Keys.Down && _historyIndex < _history.Count)
        {
            _historyIndex++;
            box.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
            box.SelectionStart = box.TextLength;
            e.Handled = true;
        }
    }

    private void OnMessage(RconMessage m)
    {
        var color = m.Type.ToLowerInvariant() switch
        {
            "chat" => Theme.Chat,
            "warning" => Theme.Warn,
            "error" => Theme.Err,
            _ => Theme.Text,
        };
        Append(m.Message.TrimEnd('\n'), color);
    }

    private void SetConnected(bool on, string text)
    {
        _connect.Enabled = !on;
        _disconnect.Enabled = on;
        _input.Box.Enabled = on;
        _send.Enabled = on;
        SetStatusText(text, on ? Theme.Ok : Theme.Muted);
        if (on) _input.Box.Focus();
    }

    private void SetStatusText(string text, Color c) { _status.Text = "●  " + text; _status.ForeColor = c; }

    private void Append(string text, Color color)
    {
        if (_console.TextLength > MaxChars)
        {
            _console.Select(0, _console.TextLength / 2);
            _console.SelectedText = "";
        }
        _console.SelectionStart = _console.TextLength;
        _console.SelectionLength = 0;
        _console.SelectionColor = Color.FromArgb(90, 96, 108);
        _console.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  ");
        _console.SelectionColor = color;
        _console.AppendText(text + Environment.NewLine);
        if (_autoScroll.Checked) _console.ScrollToCaret();
    }
}
