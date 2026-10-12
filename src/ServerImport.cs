using System.Drawing;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>A server found in another program's files, ready to be reviewed and imported.</summary>
public sealed record ImportedServer(string Name, string Host, int Port, string Source, string Note);

/// <summary>Reads server details (name, address, RCON port) from other programs. Passwords are never read: other tools store them in their own scrambled form.</summary>
public static class ServerImporters
{
    private static readonly Regex HostPattern = new(@"^[A-Za-z0-9._:\-\[\]]{1,253}$", RegexOptions.Compiled);

    /// <summary>
    /// RustAdmin keeps one file per server, <c>userCfg\cfg_&lt;server name&gt;.ini</c>, next to RustAdmin.exe. Accepts the RustAdmin folder,
    /// its userCfg folder, or one of those files.
    /// </summary>
    public static List<ImportedServer> ReadRustAdmin(string path)
    {
        var found = new List<ImportedServer>();
        foreach (var file in RustAdminFiles(path))
        {
            try { if (ReadRustAdminFile(file) is { } server) found.Add(server); }
            catch { /* skip a file that cannot be read */ }
        }
        return found;
    }

    private static IEnumerable<string> RustAdminFiles(string path)
    {
        if (File.Exists(path)) return new[] { path };
        if (!Directory.Exists(path)) return Array.Empty<string>();
        var direct = Directory.GetFiles(path, "cfg_*.ini");
        if (direct.Length > 0) return direct.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        var sub = Path.Combine(path, "userCfg");
        return Directory.Exists(sub) ? Directory.GetFiles(sub, "cfg_*.ini").OrderBy(f => f, StringComparer.OrdinalIgnoreCase) : Array.Empty<string>();
    }

    private static ImportedServer? ReadRustAdminFile(string file)
    {
        string? ip = null, port = null, version = null;
        var inGeneral = false;
        // Only the three values we need are read; nothing else in the file (passwords, API keys) is looked at.
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inGeneral = line.Equals("[General]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inGeneral) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            if (key.Equals("ip", StringComparison.OrdinalIgnoreCase)) ip = value;
            else if (key.Equals("port", StringComparison.OrdinalIgnoreCase)) port = value;
            else if (key.Equals("ServerVersion", StringComparison.OrdinalIgnoreCase)) version = value;
        }

        if (string.IsNullOrWhiteSpace(ip) || !HostPattern.IsMatch(ip)) return null;
        if (!int.TryParse(port, out var p) || p is < 1 or > 65535) return null;

        var name = Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith("cfg_", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        var note = version is { Length: > 0 } && !version.Contains("Web", StringComparison.OrdinalIgnoreCase)
            ? "Uses the old RCON style. Bunker needs WebRCON (rcon.web 1)."
            : "";
        return new ImportedServer(name.Trim(), ip.Trim(), p, "RustAdmin", note);
    }
}

/// <summary>Lets you pick servers found in another program, type each one's password and import them in one go.</summary>
public class ImportServersForm : Form
{
    private readonly Core _core;
    private readonly DarkGrid _grid = new();
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _import = Theme.PrimaryButton("Import selected");

    public List<ServerProfile> Result { get; } = new();

    public ImportServersForm(Core core)
    {
        _core = core;
        Theme.Apply(this);
        Text = "Import servers";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(700, 440);

        var intro = new Label
        {
            Text = "Bring your servers over from another program. Choose the program's folder and Bunker lists the servers it finds. " +
                   "Passwords are stored scrambled by the other program, so Bunker can't read them: type each one below.",
            Dock = DockStyle.Top, Height = 62, ForeColor = Theme.Muted, Padding = new Padding(2, 4, 2, 0),
        };

        var rustAdmin = Theme.PrimaryButton("Import from RustAdmin...");
        rustAdmin.Width = 220;
        rustAdmin.Height = 34;
        rustAdmin.Click += (_, _) => ChooseRustAdmin();
        var top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg };
        rustAdmin.Location = new Point(0, 6);
        top.Controls.Add(rustAdmin);

        _grid.ReadOnly = false;
        _grid.Dock = DockStyle.Fill;
        _grid.RowTemplate.Height = 34;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Import", FillWeight = 40 });
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("Address", "Address");
        _grid.Columns.Add("Password", "RCON password (type it here)");
        _grid.Columns.Add("Note", "");
        _grid.Columns[1].FillWeight = 150;
        _grid.Columns[2].FillWeight = 100;
        _grid.Columns[3].FillWeight = 110;
        _grid.Columns[4].FillWeight = 120;
        foreach (var c in new[] { 1, 2, 4 }) _grid.Columns[c].ReadOnly = true;
        _grid.Columns[3].DefaultCellStyle.BackColor = Theme.Input; // looks like a field you can type in
        _grid.CellFormatting += (_, e) =>
        {
            // Show typed passwords as dots.
            if (e.ColumnIndex == 3 && e.Value is string { Length: > 0 } s && !(_grid.CurrentCell?.RowIndex == e.RowIndex && _grid.IsCurrentCellInEditMode))
                e.Value = new string('•', Math.Min(s.Length, 24));
        };
        _grid.EditingControlShowing += (_, e) => { if (_grid.CurrentCell?.ColumnIndex == 3 && e.Control is TextBox t) t.UseSystemPasswordChar = true; };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

        _import.Height = 34;
        _import.Enabled = false;
        var cancel = Theme.GhostButton("Cancel");
        cancel.Height = 34;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Bg, FlowDirection = FlowDirection.RightToLeft };
        cancel.Margin = new Padding(8, 0, 0, 0);
        bar.Controls.Add(cancel);
        bar.Controls.Add(_import);
        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Theme.Bg };
        statusHost.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 10), BackColor = Theme.Bg };
        body.Controls.Add(_grid);
        body.Controls.Add(statusHost);
        body.Controls.Add(bar);
        body.Controls.Add(top);
        body.Controls.Add(intro);
        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });

        CancelButton = cancel;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _import.Click += (_, _) => DoImport();
        _status.Text = "Nothing loaded yet.";
    }

    private void ChooseRustAdmin()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose your RustAdmin folder (the one that contains RustAdmin.exe).",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        LoadRustAdmin(dialog.SelectedPath);
    }

    internal void LoadRustAdmin(string path)
    {
        var servers = ServerImporters.ReadRustAdmin(path);
        _grid.Rows.Clear();
        if (servers.Count == 0)
        {
            _status.ForeColor = Theme.Warn;
            _status.Text = "No RustAdmin servers were found in that folder. Look for the folder that contains RustAdmin.exe and a userCfg folder.";
            _import.Enabled = false;
            return;
        }

        foreach (var s in servers)
        {
            var exists = _core.Settings.Servers.Any(x => x.Host.Equals(s.Host, StringComparison.OrdinalIgnoreCase) && x.Port == s.Port);
            var note = exists ? "Already in Bunker" : s.Note;
            var i = _grid.Rows.Add(!exists, s.Name, $"{s.Host}:{s.Port}", "", note);
            _grid.Rows[i].Tag = s;
            if (exists) _grid.Rows[i].Cells[0].ReadOnly = true;
            if (note.Length > 0) _grid.Rows[i].Cells[4].Style.ForeColor = exists ? Theme.Muted : Theme.Warn;
        }
        _status.ForeColor = Theme.Muted;
        _status.Text = $"Found {servers.Count} server{(servers.Count == 1 ? "" : "s")}. Type each password, tick the ones you want and press Import.";
        _import.Enabled = true;
    }

    private void DoImport()
    {
        _grid.EndEdit();
        var chosen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is ImportedServer && r.Cells[0].Value is true).ToList();
        if (chosen.Count == 0) { _status.ForeColor = Theme.Warn; _status.Text = "Tick at least one server to import."; return; }

        var noPassword = chosen.Count(r => string.IsNullOrEmpty(r.Cells[3].Value as string));
        if (noPassword > 0)
        {
            var ask = MessageBox.Show(this,
                $"{noPassword} of the selected servers have no password yet.\n\nThey will still be added, but Bunker can't connect to them until you add the password (Settings → Servers → Edit).\n\nContinue?",
                "Missing passwords", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;
        }

        foreach (var row in chosen)
        {
            var s = (ImportedServer)row.Tag!;
            Result.Add(new ServerProfile { Name = s.Name, Host = s.Host, Port = s.Port, Password = (row.Cells[3].Value as string) ?? "" });
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
