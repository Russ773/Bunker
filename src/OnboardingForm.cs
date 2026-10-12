using System.Drawing;

namespace Bunker;

/// <summary>First-run welcome screen (also used to edit the server later). Tests the connection before saving.</summary>
public class OnboardingForm : Form
{
    private readonly ModernField _name = new();
    private readonly ModernField _host = new();
    private readonly ModernField _port = new();
    private readonly ModernField _password = new();
    private readonly Button _go = Theme.PrimaryButton("Test && connect");
    private readonly Button _skip = Theme.GhostButton("Skip for now");
    private readonly Button _saveAnyway = Theme.GhostButton("Save without testing");
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(400, 0), Font = Theme.Small, Margin = new Padding(0, 14, 0, 0) };

    private readonly string? _existingId;

    public ServerProfile? Result { get; private set; }

    public OnboardingForm(ServerProfile? existing, bool firstRun)
    {
        Theme.Apply(this);
        _existingId = existing?.Id;
        Text = firstRun ? "Welcome to Bunker" : "Server details";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 700);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(40, 34, 40, 20),
        };

        flow.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Accent, AutoSize = true });
        flow.Controls.Add(new Label
        {
            Text = firstRun ? "Let's connect your server" : "Server details",
            Font = Theme.Title, AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 10, 0, 0),
        });
        flow.Controls.Add(new Label
        {
            Text = firstRun
                ? "Bunker lets you watch the console and manage your Rust server without being in the game. Enter your server's RCON details to get started."
                : "Change the server Bunker connects to. This applies the next time you connect.",
            ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 8, 0, 6),
        });

        flow.Controls.Add(Caption("Server name (optional)"));
        flow.Controls.Add(Sized(_name));

        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Width = 400, AutoSize = true, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        row.Controls.Add(Caption("Server address"), 0, 0);
        row.Controls.Add(Caption("RCON port"), 1, 0);
        _host.Dock = DockStyle.Top; _host.Margin = new Padding(0, 0, 10, 0);
        _port.Dock = DockStyle.Top; _port.Margin = new Padding(0);
        row.Controls.Add(_host, 0, 1);
        row.Controls.Add(_port, 1, 1);
        flow.Controls.Add(row);

        flow.Controls.Add(Caption("RCON password"));
        var toggle = new Label { Text = "Show", ForeColor = Theme.Muted, Width = 46, TextAlign = ContentAlignment.MiddleRight, Cursor = Cursors.Hand, Font = Theme.Small };
        _password.AddTrailing(toggle);
        _password.Box.UseSystemPasswordChar = true;
        toggle.Click += (_, _) =>
        {
            _password.Box.UseSystemPasswordChar = !_password.Box.UseSystemPasswordChar;
            toggle.Text = _password.Box.UseSystemPasswordChar ? "Show" : "Hide";
        };
        flow.Controls.Add(Sized(_password));

        flow.Controls.Add(new Label
        {
            Text = "Your server needs WebRCON enabled. Add these to its startup parameters:\n+rcon.web 1  +rcon.port 28016  +rcon.password \"yourpassword\"",
            ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 14, 0, 0),
        });

        flow.Controls.Add(_status);

        _go.Width = 400; _go.Height = 42; _go.Margin = new Padding(0, 20, 0, 0);
        flow.Controls.Add(_go);
        _saveAnyway.Width = 400; _saveAnyway.Margin = new Padding(0, 10, 0, 0); _saveAnyway.Visible = false;
        flow.Controls.Add(_saveAnyway);
        _skip.Width = 400; _skip.Margin = new Padding(0, 10, 0, 0);
        _skip.Text = firstRun ? "Skip for now" : "Cancel";
        flow.Controls.Add(_skip);

        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });

        _name.Box.PlaceholderText = "My Rust Server";
        _host.Box.PlaceholderText = "123.45.67.89 or play.example.com";
        _port.Box.PlaceholderText = "28016";
        _name.Text = existing?.Name ?? "";
        _host.Text = existing?.Host ?? "";
        _port.Text = (existing?.Port ?? 28016).ToString();
        _password.Text = existing?.Password ?? "";

        AcceptButton = _go;
        CancelButton = _skip;
        _go.Click += async (_, _) => await TestAndSaveAsync();
        _skip.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _saveAnyway.Click += (_, _) => SaveWithoutTesting();
        Shown += (_, _) => _host.Box.Focus();
    }

    private static Label Caption(string text) =>
        new() { Text = text, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 14, 0, 4) };

    private static Control Sized(Control c) { c.Width = 400; c.Margin = new Padding(0); return c; }

    private void Say(string text, Color color) { _status.Text = text; _status.ForeColor = color; }

    private async Task TestAndSaveAsync()
    {
        var host = _host.Text.Trim();
        if (host.Length == 0) { Say("Enter your server's IP address or hostname.", Theme.Err); _host.Box.Focus(); return; }
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
        { Say("The RCON port must be a number between 1 and 65535.", Theme.Err); _port.Box.Focus(); return; }

        _go.Enabled = _skip.Enabled = false;
        Say("Connecting...", Theme.Warn);

        using var rcon = new RconClient();
        var dropped = false;
        rcon.Disconnected += _ => dropped = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await rcon.ConnectAsync(host, port, _password.Text, cts.Token);
            await Task.Delay(700); // Rust drops the socket straight away if the password is wrong.
            if (dropped) { Say("The server closed the connection. Check the RCON password.", Theme.Err); _saveAnyway.Visible = true; return; }
        }
        catch (OperationCanceledException)
        {
            Say($"Timed out reaching {host}:{port}. Check the address, port and firewall.", Theme.Err);
            _saveAnyway.Visible = true;
            return;
        }
        catch (Exception e)
        {
            Say($"Couldn't connect to {host}:{port}. Is WebRCON enabled and the port open?\n({e.Message})", Theme.Err);
            _saveAnyway.Visible = true;
            return;
        }
        finally
        {
            _go.Enabled = _skip.Enabled = true;
        }

        Accept(host, port);
    }

    /// <summary>For a server that is offline right now: keep the details without a successful test.</summary>
    private void SaveWithoutTesting()
    {
        var host = _host.Text.Trim();
        if (host.Length == 0) { Say("Enter your server's IP address or hostname.", Theme.Err); return; }
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
        { Say("The RCON port must be a number between 1 and 65535.", Theme.Err); return; }
        Accept(host, port);
    }

    private void Accept(string host, int port)
    {
        Result = new ServerProfile { Id = _existingId ?? Guid.NewGuid().ToString("N"), Name = _name.Text.Trim(), Host = host, Port = port, Password = _password.Text };
        DialogResult = DialogResult.OK;
        Close();
    }
}
