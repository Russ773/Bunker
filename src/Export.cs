using System.Globalization;
using System.Text;

namespace Bunker;

/// <summary>Turns what Bunker knows into files you can keep: the console as text, and the player database as a spreadsheet (CSV).</summary>
public static class ExportText
{
    /// <summary>One console line as it would be read in a file: the date and time, then the text (chat the way it looks in the game).</summary>
    public static string LogLine(LogEntry e)
    {
        var stamp = e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (e.Kind == LogKind.Chat && !string.IsNullOrEmpty(e.UserName) && e.Message is not null)
        {
            var prefix = (e.Channel == 1 ? "[TEAM] " : "") + (string.IsNullOrEmpty(e.Tags) ? "" : e.Tags + " ");
            return $"{stamp}  {prefix}{e.UserName}: {e.Message}";
        }
        return $"{stamp}  {e.Text}";
    }

    public static string LogText(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries) sb.AppendLine(LogLine(e));
        return sb.ToString();
    }

    private static readonly string[] PlayerColumns = { "Steam ID", "Name", "First seen", "Last seen", "Hours played", "Visits", "Online now", "Note", "Steam profile" };

    /// <summary>The player database as CSV. Opens in Excel, Google Sheets or LibreOffice.</summary>
    public static string PlayersCsv(IEnumerable<PlayerRecord> players, Func<string, bool> isOnline)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", PlayerColumns.Select(c => Cell(c))));
        foreach (var p in players)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                SteamIdCell(p.SteamId),
                Cell(p.Name),
                Cell(p.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Cell(p.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Cell((p.TotalSeconds / 3600).ToString("0.00", CultureInfo.InvariantCulture)),
                Cell(p.Sessions.ToString(CultureInfo.InvariantCulture)),
                Cell(isOnline(p.SteamId) ? "Yes" : "No"),
                Cell(p.Note),
                Cell(p.SteamId.All(char.IsDigit) && p.SteamId.Length > 0 ? "https://steamcommunity.com/profiles/" + p.SteamId : ""),
            }));
        }
        return sb.ToString();
    }

    /// <summary>
    /// One CSV cell. Quotes anything with a comma, quote or line break. A value that starts with = + - or @ would be run as a formula by a
    /// spreadsheet (a player could pick such a name on purpose), so it gets a leading apostrophe, which shows as plain text.
    /// </summary>
    public static string Cell(string? value)
    {
        var v = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (v.Length > 0 && "=+-@\t".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>Excel turns a 17-digit number into 7.65612E+16 and loses the end. Written as ="7656..." it stays as the text it is.</summary>
    private static string SteamIdCell(string id) => id.Length > 0 && id.All(char.IsDigit) ? $"=\"{id}\"" : Cell(id);

    /// <summary>Where saved files go by default: a Bunker folder inside Documents, so the rest of the user's folders stay tidy.</summary>
    public static string FolderIn(string documents) => Path.Combine(documents, "Bunker", "Exports");

    /// <summary>The export folder, created if it is not there yet. If it cannot be made (a locked-down PC), Documents itself.</summary>
    public static string EnsureFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = FolderIn(documents);
        try { Directory.CreateDirectory(folder); return folder; }
        catch { return documents; }
    }

    /// <summary>A file name made from a title and the time, with nothing Windows would refuse.</summary>
    public static string FileName(string title, DateTime now, string extension)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(title.Select(c => bad.Contains(c) || c == ' ' ? '-' : c).ToArray()).Trim('-');
        return $"{(clean.Length == 0 ? "bunker" : clean)}-{now:yyyy-MM-dd-HHmm}.{extension.TrimStart('.')}";
    }
}
