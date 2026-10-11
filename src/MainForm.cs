using System.Drawing;

namespace ChaosAdmin;

public class MainForm : Form
{
    private const int MaxChars = 500_000;

    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 130 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 28016, Width = 70 };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 120 };
    private readonly Button _connect = new() { Text = "Connect", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnect", AutoSize = true, Enabled = false };
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };
    private readonly RichTextBox _console = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.Gainsboro,
        Font = new Font("Consolas", 10f), HideSelection = false, DetectUrls = false,
    };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), Enabled = false };
    private readonly Button _send = new() { Text = "Send", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { Text = "Disconnected", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left };

    private readonly Button _settingsBtn = new() { Text = "Settings", AutoSize = true };
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly RconClient _rcon = new();
    private readonly List<string> _history = new();
    private int _historyIndex;

    public MainForm()
    {
        Text = "Chaos Admin v0.0.1";
        Size = new Size(1000, 650);
        MinimumSize = new Size(700, 400);
        BackColor = Color.FromArgb(29, 32, 38);

        foreach (var c in new Control[] { _host, _password, _port })
        {
            c.BackColor = Color.FromArgb(20, 22, 26);
            c.ForeColor = Color.Gainsboro;
        }
        _input.BackColor = Color.FromArgb(20, 22, 26);
        _input.ForeColor = Color.Gainsboro;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        top.Controls.AddRange(new Control[]
        {
            Lbl("Host"), _host, Lbl("Port"), _port, Lbl("Password"), _password,
            _connect, _disconnect, _autoScroll, _clear, _settingsBtn, _status,
        });

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);

        Controls.Add(_console);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = _send;

        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += async (_, _) => { await _rcon.DisconnectAsync(); SetConnected(false, "Disconnected"); };
        _clear.Click += (_, _) => _console.Clear();
        _send.Click += async (_, _) => await SendAsync();
        _input.KeyDown += InputKeyDown;
        _rcon.MessageReceived += m => BeginInvoke(() => OnMessage(m));
        _rcon.Disconnected += reason => BeginInvoke(() => { Append(reason, Color.Gray); SetConnected(false, reason); });
        _settingsBtn.Click += (_, _) => { using var f = new SettingsForm(_settings); f.ShowDialog(this); };
        Shown += async (_, _) => await AutoUpdateAsync();
        FormClosing += (_, _) => _rcon.Dispose();
    }

    private async Task AutoUpdateAsync()
    {
        if (!_settings.CheckForUpdatesOnLaunch) return;
        try
        {
            var update = await Updater.CheckAsync();
            if (update is null) return;
            Append($"Update {update.Version} found - downloading and installing...", Color.Goldenrod);
            SetStatus("Updating...", Color.Goldenrod);
            await Updater.DownloadAndLaunchAsync(update);
            Application.Exit();
        }
        catch (Exception e)
        {
            Append("Update check failed: " + e.Message, Color.Gray);
            SetStatus("Disconnected", Color.Gray);
        }
    }

    private static Label Lbl(string t) => new() { Text = t, AutoSize = true, ForeColor = Color.Gainsboro, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 0, 0) };

    private async Task ConnectAsync()
    {
        _connect.Enabled = false;
        SetStatus("Connecting...", Color.Goldenrod);
        try
        {
            await _rcon.ConnectAsync(_host.Text.Trim(), (int)_port.Value, _password.Text);
            Append($"Connected to {_host.Text}:{_port.Value}", Color.Gray);
            SetConnected(true, "Connected");
            await _rcon.SendCommandAsync("serverinfo");
        }
        catch (Exception e)
        {
            Append("Connection failed: " + e.Message, Color.IndianRed);
            SetConnected(false, "Connection failed");
        }
    }

    private async Task SendAsync()
    {
        var cmd = _input.Text.Trim();
        if (cmd.Length == 0 || !_rcon.IsConnected) return;
        Append("> " + cmd, Color.Gray);
        _history.Add(cmd);
        _historyIndex = _history.Count;
        _input.Clear();
        try { await _rcon.SendCommandAsync(cmd); }
        catch (Exception e) { Append("Send failed: " + e.Message, Color.IndianRed); }
    }

    private void InputKeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && _historyIndex > 0) { _input.Text = _history[--_historyIndex]; _input.SelectionStart = _input.TextLength; e.Handled = true; }
        else if (e.KeyCode == Keys.Down && _historyIndex < _history.Count) { _historyIndex++; _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : ""; _input.SelectionStart = _input.TextLength; e.Handled = true; }
    }

    private void OnMessage(RconMessage m)
    {
        var color = m.Type.ToLowerInvariant() switch
        {
            "chat" => Color.LightSkyBlue,
            "warning" => Color.Goldenrod,
            "error" => Color.IndianRed,
            _ => Color.Gainsboro,
        };
        Append(m.Message.TrimEnd('\n'), color);
    }

    private void SetConnected(bool on, string text)
    {
        _connect.Enabled = !on;
        _disconnect.Enabled = on;
        _input.Enabled = on;
        _send.Enabled = on;
        SetStatus(text, on ? Color.MediumSeaGreen : Color.Gray);
        if (on) _input.Focus();
    }

    private void SetStatus(string text, Color c) { _status.Text = text; _status.ForeColor = c; }

    private void Append(string text, Color color)
    {
        if (_console.TextLength > MaxChars)
        {
            _console.Select(0, _console.TextLength / 2);
            _console.SelectedText = "";
        }
        _console.SelectionStart = _console.TextLength;
        _console.SelectionLength = 0;
        _console.SelectionColor = Color.DimGray;
        _console.AppendText(DateTime.Now.ToString("HH:mm:ss") + " ");
        _console.SelectionColor = color;
        _console.AppendText(text + Environment.NewLine);
        if (_autoScroll.Checked) _console.ScrollToCaret();
    }
}
