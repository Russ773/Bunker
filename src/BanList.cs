using System.Globalization;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>Anything a reply can be turned into that the reply table can show: a heading row, rows, and what a click on a row picks.</summary>
public interface IReplyTable
{
    string[] Columns { get; }
    List<string[]> Rows { get; }
    /// <summary>The column whose text is passed on when a row is clicked (a plugin's name, a player's Steam ID).</summary>
    int PickColumn { get; }
    /// <summary>The column shown in bold, the one that says what each row is.</summary>
    int BoldColumn { get; }
    /// <summary>A column to show in a quieter colour, or -1.</summary>
    int MutedColumn { get; }
    string Summary { get; }
}

/// <summary>The server's ban list (<c>banlistex</c>) read into a table.</summary>
public sealed class BanList : IReplyTable
{
    public string[] Columns { get; } = { "#", "Steam ID", "Name", "Reason", "Expires" };
    public List<string[]> Rows { get; } = new();
    public int PickColumn => 1;
    public int BoldColumn => 2;
    public int MutedColumn => 0;
    public int Permanent { get; set; }
    public int Temporary { get; set; }

    public string Summary
    {
        get
        {
            if (Rows.Count == 0) return "Nobody is banned";
            var parts = new List<string> { Rows.Count == 1 ? "1 ban" : $"{Rows.Count} bans" };
            if (Permanent > 0) parts.Add($"{Permanent} permanent");
            if (Temporary > 0) parts.Add($"{Temporary} temporary");
            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>
/// Reads the lines <c>banlistex</c> prints: <c>1 76561198000000000 "name" "reason" -1</c>. The last number is when the ban ends,
/// as a Unix time, or -1 for a ban that never ends. Anything else is left for the caller to show as text.
/// </summary>
public static class BanListParser
{
    // An optional clock time in front is allowed, because the console adds one when it prints a reply.
    private static readonly Regex Line = new(@"^\s*(?:\d{1,2}:\d{2}:\d{2}\s+)?(\d+)\s+(\d{15,20})\s+""(.*?)""\s+""(.*)""\s+(-?\d+)\s*$", RegexOptions.Compiled);

    public static BanList? TryParse(string reply, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(reply) || reply.Length > 400_000) return null;
        var lines = reply.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return null;

        var list = new BanList();
        var current = now ?? DateTimeOffset.Now;
        foreach (var line in lines)
        {
            var m = Line.Match(line);
            if (!m.Success) return null; // a reply with anything else in it is not a ban list; show it as it came
            var expiry = long.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            if (expiry < 0) list.Permanent++; else list.Temporary++;
            list.Rows.Add(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, Expires(expiry, current) });
        }
        return list;
    }

    /// <summary>"Permanent", or the date the ban ends (sortable: year first).</summary>
    public static string Expires(long expiry, DateTimeOffset now)
    {
        if (expiry < 0) return "Permanent";
        if (expiry < 1_000_000_000) return "in " + TimeSpan.FromSeconds(expiry).ToString(@"d\.hh\:mm\:ss").TrimStart('0', '.', ':'); // not a date: some servers print the time left
        var end = DateTimeOffset.FromUnixTimeSeconds(expiry).ToLocalTime();
        return end.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (end < now ? " (expired)" : "");
    }
}

/// <summary>Recognises the replies the reply table can show.</summary>
public static class ReplyTables
{
    public static IReplyTable? TryParse(string reply) => (IReplyTable?)PluginListParser.TryParse(reply) ?? BanListParser.TryParse(reply);
}
