using System.Globalization;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>A plugin that failed to compile: the file, where, and what the compiler said.</summary>
public sealed record PluginProblem(string File, string Line, string Message);

/// <summary>A plugin list read out of a server reply, ready to show as a table.</summary>
public sealed class PluginList : IReplyTable
{
    public string Framework { get; init; } = "";
    public string[] Columns { get; init; } = Array.Empty<string>();

    /// <summary>Which column holds the plugin's name (what you type into Reload, Load and Unload).</summary>
    public int NameColumn { get; init; }
    int IReplyTable.PickColumn => NameColumn;
    int IReplyTable.BoldColumn => NameColumn;
    int IReplyTable.MutedColumn => Columns.Length > 0 && Columns[0] == "Type" ? 0 : -1; // the kind of plugin reads better in a quieter colour than its name

    public List<string[]> Rows { get; } = new();
    public List<string> Unloaded { get; } = new();
    public List<PluginProblem> Failed { get; } = new();

    public string Summary
    {
        get
        {
            var parts = new List<string> { Rows.Count == 0 ? "No plugins loaded" : $"{Rows.Count} plugin{(Rows.Count == 1 ? "" : "s")} loaded" };
            if (Unloaded.Count > 0) parts.Add($"{Unloaded.Count} not loaded");
            var files = Failed.Select(f => f.File).Distinct().Count();
            if (files > 0) parts.Add($"{files} failed to compile");
            return string.Join("  ·  ", parts) + (Framework.Length > 0 ? $"  ·  {Framework}" : "");
        }
    }
}

/// <summary>
/// Turns the plain-text plugin lists that Carbon (<c>c.plugins</c>) and Oxide (<c>oxide.plugins</c>) print into a table.
/// Carbon's layout was taken from a real server reply. Oxide's is the commonly documented one and has not been checked on a live
/// server. If a reply is not recognised, the caller falls back to showing the text as it came.
/// </summary>
public static class PluginListParser
{
    private static readonly Regex Cell = new(@"\S+(?: \S+)*", RegexOptions.Compiled);
    private static readonly Regex GroupMarker = new(@"^(\d+|\*)\s{2,}\S", RegexOptions.Compiled);
    private static readonly Regex Count = new(@"\s*\(\d+\)$", RegexOptions.Compiled);
    private static readonly Regex OxideRow = new(@"^\s*(\d+)\s+""(.+?)""\s+\(([^)]*)\)\s+by\s+(.+?)\s+\(([^)]*)\)(?:\s+-\s+(.+?))?\s*$", RegexOptions.Compiled);

    public static PluginList? TryParse(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || reply.Length > 400_000) return null;
        var lines = reply.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return Carbon(lines) ?? Oxide(lines);
    }

    // ---------------------------------------------------------------- Carbon

    private static readonly Dictionary<string, string> CarbonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["package"] = "Plugin", ["author"] = "Author", ["version"] = "Version", ["hook time"] = "Hook time", ["hook fires"] = "Hook fires",
        ["hook memory"] = "Memory", ["hook lag"] = "Hook lag", ["hook exceptions"] = "Exceptions", ["compile time"] = "Compile time", ["uptime"] = "Uptime",
    };

    /// <summary>
    /// Carbon prints one aligned table: a header row, then groups (a number and a name, such as "2  Scripts (105)") each followed by
    /// its plugins indented by three spaces, then "*" groups for plugins that were not loaded or failed to compile. Every column
    /// starts under its header, so cells are cut out by the header's positions.
    /// </summary>
    private static PluginList? Carbon(string[] lines)
    {
        var headerAt = Array.FindIndex(lines, l => l.TrimStart().StartsWith('#') && l.Contains("package") && l.Contains("author"));
        if (headerAt < 0) return null;

        var columns = Cell.Matches(lines[headerAt]).Select(m => (Name: m.Value, Start: m.Index)).Skip(1).ToList(); // skip the "#"
        if (columns.Count < 2) return null;

        var rows = new List<string[]>();
        var unloaded = new List<string>();
        var failed = new List<PluginProblem>();
        var group = "";
        var mode = "plugins";
        var lineStart = 0;
        var messageStart = 0;

        for (var i = headerAt + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0) continue;

            if (GroupMarker.IsMatch(line))
            {
                var tokens = Cell.Matches(line).ToList();
                var marker = tokens[0].Value;
                var title = tokens.Count > 1 ? tokens[1].Value : "";
                if (marker == "*")
                {
                    mode = title.StartsWith("unloaded", StringComparison.OrdinalIgnoreCase) ? "unloaded"
                         : title.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "other";
                    if (mode == "failed" && tokens.Count >= 4) { lineStart = tokens[2].Index; messageStart = tokens[3].Index; }
                }
                else { mode = "plugins"; group = Count.Replace(title, ""); }
                continue;
            }

            if (!line.StartsWith("   ") || line.Trim().Length == 0) continue; // plugin rows are indented by three spaces

            switch (mode)
            {
                case "plugins":
                {
                    var cells = new string[columns.Count + 1];
                    cells[0] = group;
                    for (var c = 0; c < columns.Count; c++)
                        cells[c + 1] = Slice(line, columns[c].Start, c + 1 < columns.Count ? columns[c + 1].Start : -1);
                    if (cells[1].Length > 0) rows.Add(cells);
                    break;
                }
                case "unloaded":
                    unloaded.Add(line.Trim());
                    break;
                case "failed" when lineStart > 0 && messageStart > lineStart:
                    failed.Add(new PluginProblem(Slice(line, 3, lineStart), Slice(line, lineStart, messageStart), Slice(line, messageStart, -1)));
                    break;
            }
        }

        // Columns that are empty for every plugin (for example "exceptions" on a healthy server) are not worth showing.
        var keep = new List<int> { 0 };
        for (var c = 1; c <= columns.Count; c++)
            if (c == 1 || rows.Any(r => r[c].Length > 0)) keep.Add(c);

        var list = new PluginList
        {
            Framework = "Carbon",
            NameColumn = 1,
            Columns = keep.Select(c => c == 0 ? "Type" : CarbonNames.TryGetValue(columns[c - 1].Name, out var nice) ? nice : Capitalise(columns[c - 1].Name)).ToArray(),
        };
        foreach (var r in rows) list.Rows.Add(keep.Select(c => r[c]).ToArray());
        list.Unloaded.AddRange(unloaded);
        list.Failed.AddRange(failed);
        return list;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return "";
        var stop = end < 0 || end > line.Length ? line.Length : end;
        return stop <= start ? "" : line[start..stop].Trim();
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------------------------------------------------------------- Oxide

    /// <summary>Oxide prints "Listing N plugins:" then one line per plugin: <c>01 "Name" (1.0.0) by Author (0.29s / 10 KiB) - File.cs</c>.</summary>
    private static PluginList? Oxide(string[] lines)
    {
        var list = new PluginList { Framework = "Oxide", NameColumn = 0, Columns = new[] { "Plugin", "Version", "Author", "Load time", "Memory", "File" } };
        foreach (var line in lines)
        {
            var m = OxideRow.Match(line);
            if (!m.Success) continue;
            var stats = m.Groups[5].Value.Split('/', 2);
            list.Rows.Add(new[]
            {
                m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim(), m.Groups[4].Value.Trim(),
                stats[0].Trim(), stats.Length > 1 ? stats[1].Trim() : "", m.Groups[6].Value.Trim(),
            });
        }
        return list.Rows.Count == 0 ? null : list;
    }

    // ---------------------------------------------------------------- sorting

    private static readonly Regex Duration = new(@"^(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.Compiled);
    private static readonly Regex Quantity = new(@"^([\d,]*\.?\d+)\s*(ms|s|b|kb|kib|mb|mib|gb|gib)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A version is "v1.2.3" or "1.2.3"; a lone number such as "2" is a count, not a version.
    private static readonly Regex VersionText = new(@"^(?:v(\d+(?:\.\d+)*)|(\d+(?:\.\d+)+))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A number to sort a cell by, so "9ms" comes before "1092ms", "40.0kb" before "4.2mb" and "1m45s" before "11h50m". Cells that
    /// are not numbers return null and are sorted as text.
    /// </summary>
    public static double? SortValue(string cell)
    {
        var s = cell.Trim();
        if (s.Length == 0) return null;

        var d = Duration.Match(s);
        if (d.Success && d.Groups.Cast<Group>().Skip(1).Any(g => g.Success))
        {
            double G(int i) => d.Groups[i].Success ? double.Parse(d.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return ((G(1) * 24 + G(2)) * 60 + G(3)) * 60 + G(4);
        }

        var v = VersionText.Match(s);
        if (v.Success)
        {
            double total = 0, scale = 1_000_000;
            foreach (var part in (v.Groups[1].Success ? v.Groups[1].Value : v.Groups[2].Value).Split('.')) { total += double.Parse(part, CultureInfo.InvariantCulture) * scale; scale /= 1000; }
            return total;
        }

        var q = Quantity.Match(s);
        if (!q.Success) return null;
        var n = double.Parse(q.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        return q.Groups[2].Value.ToLowerInvariant() switch
        {
            "s" => n * 1000,
            "kb" or "kib" => n * 1024,
            "mb" or "mib" => n * 1024 * 1024,
            "gb" or "gib" => n * 1024 * 1024 * 1024,
            _ => n,
        };
    }
}
