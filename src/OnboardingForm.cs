using System.Drawing;
using System.Reflection;

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
    private readonly CheckBox _another = new() { Text = "I have more servers to add", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(0, 16, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(400, 0), Font = Theme.Small, Margin = new Padding(0, 14, 0, 0) };

    private readonly string? _existingId;

    public ServerProfile? Result { get; private set; }

    /// <summary>The box "I have more servers to add" was ticked: the caller shows the form again for the next server.</summary>
    public bool AddAnother => _another.Checked;

    public OnboardingForm(ServerProfile? existing, bool firstRun, bool addingAnother = false)
    {
        Theme.Apply(this);
        _existingId = existing?.Id;
        Text = firstRun ? "Welcome to Bunker" : addingAnother ? "Add another server" : "Server details";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // The welcome screen is wider: the form on the left, and on the right a look at the Multi Server page.
        ClientSize = new Size(firstRun ? 1020 : 480, firstRun || addingAnother ? 760 : 700);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(40, 34, 40, 20),
        };

        flow.Controls.Add(new Label { Text = "BUNKER", Font = Theme.Brand, ForeColor = Theme.Accent, AutoSize = true });
        flow.Controls.Add(new Label
        {
            Text = firstRun ? "Let's connect your server" : addingAnother ? "Add another server" : "Server details",
            Font = Theme.Title, AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(0, 10, 0, 0),
        });
        flow.Controls.Add(new Label
        {
            Text = firstRun
                ? "Bunker lets you watch the console and manage your Rust server without being in the game. Enter your server's RCON details to get started."
                : addingAnother ? "Add your next server. Bunker shows up to six side by side on the Multi Server page."
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
        if (firstRun || addingAnother) flow.Controls.Add(_another);

        _go.Width = 400; _go.Height = 42; _go.Margin = new Padding(0, 20, 0, 0);
        flow.Controls.Add(_go);
        _saveAnyway.Width = 400; _saveAnyway.Margin = new Padding(0, 10, 0, 0); _saveAnyway.Visible = false;
        flow.Controls.Add(_saveAnyway);
        _skip.Width = 400; _skip.Margin = new Padding(0, 10, 0, 0);
        _skip.Text = firstRun ? "Skip for now" : addingAnother ? "Done" : "Cancel";
        flow.Controls.Add(_skip);

        Controls.Add(flow);
        if (firstRun) Controls.Add(BuildMultiServerPanel());
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

    /// <summary>The right-hand side of the welcome screen: tells server admins they can add more servers, and shows how that looks.</summary>
    private static Control BuildMultiServerPanel()
    {
        var panel = new Panel { Dock = DockStyle.Right, Width = 540, BackColor = Theme.Panel, Padding = new Padding(30, 34, 30, 20) };
        panel.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, 0, panel.Height); };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel };
        flow.Controls.Add(new Label { Text = "RUN MORE THAN ONE SERVER?", Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Accent, AutoSize = true });
        flow.Controls.Add(new Label { Text = "Add as many as you like", Font = Theme.Title, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 8, 0, 0) });
        flow.Controls.Add(new Label
        {
            Text = "Tick the box on this screen to add another, or add more later in Settings. The Multi Server page then shows up to six of them side by side.",
            ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 8, 0, 14),
        });
        if (LoadPreview() is { } preview)
            flow.Controls.Add(new PictureBox { Image = preview, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(480, (int)(480.0 * preview.Height / preview.Width)), Margin = new Padding(0, 0, 0, 16), BackColor = Theme.Panel });
        foreach (var line in new[] { "Who is online, FPS, uptime and a chart for each one", "Open any server with one click", "Send a message or command to all of them at once" })
            flow.Controls.Add(new Label { Text = "•  " + line, ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 2, 0, 4) });
        panel.Controls.Add(flow);
        return panel;
    }

    /// <summary>A picture of the Multi Server page, built into the app.</summary>
    private static Image? LoadPreview()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("multiserver.png");
            if (stream is null) return null;
            using var raw = Image.FromStream(stream);
            return new Bitmap(raw); // a copy, so the stream can be closed
        }
        catch { return null; }
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
