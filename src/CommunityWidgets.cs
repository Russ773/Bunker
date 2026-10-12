using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bunker;

// A community widget is a JSON file, never code. It lists its fields and buttons, and every command a button can send,
// so it can be reviewed in full before it is installed. See docs/WIDGETS.md.

public class WidgetControl
{
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public string? Label { get; set; }
    public string? Text { get; set; }
    public string? Placeholder { get; set; }
    public string? Style { get; set; }
    public string? Confirm { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Default { get; set; }
    public List<string>? Options { get; set; }
    public List<string>? Commands { get; set; }
}

public class WidgetDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<WidgetControl> Controls { get; set; } = new();
}

public enum CommandRisk { Normal, Warning, Blocked }

public static class WidgetRules
{
    public const int MaxFileBytes = 64 * 1024;
    private const int MaxControls = 30;
    private const int MaxCommandLength = 300;

    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9_-]{1,40}$", RegexOptions.Compiled);
    private static readonly Regex ControlIdPattern = new(@"^[A-Za-z][A-Za-z0-9_]{0,30}$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z][A-Za-z0-9_]{0,30})(\.name)?\}", RegexOptions.Compiled);
    private static readonly string[] Types = { "label", "separator", "text", "number", "choice", "player", "button" };

    // Commands that change who is an admin or how RCON itself is secured are never allowed from a shared widget.
    private static readonly string[] BlockedNames = { "ownerid", "moderatorid", "removeowner", "removemoderator", "global.ownerid", "global.moderatorid", "global.removeowner", "global.removemoderator" };
    // Commands that deserve an extra look before installing.
    private static readonly string[] WarningNames = { "quit", "restart", "ban", "banid", "unban", "kick", "mute", "unmute", "teleport", "inventory.giveall", "server.stop", "global.quit", "global.restart", "global.kick", "global.ban", "global.banid" };

    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    public static string CommandName(string command)
    {
        var t = command.TrimStart();
        var i = t.IndexOfAny(new[] { ' ', '\t' });
        return (i < 0 ? t : t[..i]).ToLowerInvariant();
    }

    public static CommandRisk RiskOf(string command)
    {
        var name = CommandName(command);
        if (name.StartsWith("rcon.", StringComparison.Ordinal) || name.StartsWith("global.rcon.", StringComparison.Ordinal) || BlockedNames.Contains(name))
            return CommandRisk.Blocked;
        if (WarningNames.Contains(name) || name.StartsWith("oxide.", StringComparison.Ordinal) || name.StartsWith("o.", StringComparison.Ordinal) || name.StartsWith("c.", StringComparison.Ordinal))
            return CommandRisk.Warning;
        return CommandRisk.Normal;
    }

    public static IEnumerable<string> AllCommands(WidgetDef d) =>
        d.Controls.Where(c => c.Type == "button").SelectMany(c => c.Commands ?? new List<string>());

    /// <summary>Reads and checks a widget file. Returns null (with the reasons) if it is not acceptable.</summary>
    public static WidgetDef? Parse(string json, out List<string> errors)
    {
        errors = new List<string>();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxFileBytes) { errors.Add("The file is larger than 64 KB."); return null; }
        WidgetDef? def;
        try { def = JsonSerializer.Deserialize<WidgetDef>(json, Json); }
        catch (JsonException e) { errors.Add("This is not a valid widget file: " + e.Message); return null; }
        if (def is null) { errors.Add("The file is empty."); return null; }
        errors.AddRange(Validate(def));
        return errors.Count == 0 ? def : null;
    }

    public static List<string> Validate(WidgetDef d)
    {
        var errors = new List<string>();
        if (!IdPattern.IsMatch(d.Id ?? "")) errors.Add("\"id\" must be 2-41 characters: lower-case letters, numbers, - or _.");
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 40) errors.Add("\"name\" is required (up to 40 characters).");
        if (d.Controls is null || d.Controls.Count == 0) { errors.Add("The widget has no controls."); return errors; }
        if (d.Controls.Count > MaxControls) errors.Add($"A widget can have at most {MaxControls} controls.");

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in d.Controls)
        {
            if (!Types.Contains(c.Type)) { errors.Add($"Unknown control type \"{c.Type}\"."); continue; }
            if (c.Type is "text" or "number" or "choice" or "player")
            {
                if (c.Id is null || !ControlIdPattern.IsMatch(c.Id)) { errors.Add($"A {c.Type} control needs an \"id\" (letters, numbers, _)."); continue; }
                if (!ids.TryAdd(c.Id, c.Type)) errors.Add($"The id \"{c.Id}\" is used twice.");
            }
            if (c.Type == "choice" && (c.Options is null || c.Options.Count == 0 || c.Options.Count > 50 || c.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 60)))
                errors.Add($"The choice \"{c.Id}\" needs 1-50 options.");
            if (c.Type == "number" && c.Min is { } min && c.Max is { } max && min > max) errors.Add($"The number \"{c.Id}\" has min greater than max.");
        }

        foreach (var b in d.Controls.Where(c => c.Type == "button"))
        {
            if (string.IsNullOrWhiteSpace(b.Text)) errors.Add("Every button needs \"text\".");
            if (b.Commands is null || b.Commands.Count == 0 || b.Commands.Count > 10) { errors.Add($"The button \"{b.Text}\" needs 1-10 commands."); continue; }
            foreach (var cmd in b.Commands)
            {
                if (string.IsNullOrWhiteSpace(cmd) || cmd.Length > MaxCommandLength) { errors.Add($"A command on \"{b.Text}\" is empty or too long."); continue; }
                if (RiskOf(cmd) == CommandRisk.Blocked) errors.Add($"The command \"{CommandName(cmd)}\" is not allowed in shared widgets.");
                foreach (Match m in PlaceholderPattern.Matches(cmd))
                {
                    var id = m.Groups[1].Value;
                    if (!ids.TryGetValue(id, out var type)) errors.Add($"\"{{{id}}}\" in a command on \"{b.Text}\" does not match any field.");
                    else if (m.Groups[2].Success && type != "player") errors.Add($"\"{{{id}.name}}\" only works for player fields.");
                }
            }
        }
        return errors;
    }

    /// <summary>Strips anything that could break out of a command argument.</summary>
    public static string Clean(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c) && c != '"' && c != ';' && c != '\\').ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : clean;
    }

    /// <summary>Fills a command template. Values must already be cleaned/validated by the caller.</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> names) =>
        PlaceholderPattern.Replace(template, m =>
        {
            var id = m.Groups[1].Value;
            var dict = m.Groups[2].Success ? names : values;
            return dict.TryGetValue(id, out var v) ? v : "";
        });

    public static IEnumerable<string> Placeholders(string template) =>
        PlaceholderPattern.Matches(template).Select(m => m.Groups[1].Value).Distinct();
}

/// <summary>Installed community widgets, stored as JSON files in the settings folder.</summary>
public class WidgetStore
{
    private readonly string _folder = Path.Combine(AppSettings.Folder, "widgets");
    private List<(WidgetDef Def, string Json)> _items = new();

    public event Action? Changed;

    public string Folder => _folder;
    public IReadOnlyList<WidgetDef> All => _items.Select(i => i.Def).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public WidgetDef? Get(string? id) => id is null ? null : _items.FirstOrDefault(i => i.Def.Id == id).Def;

    public WidgetStore() => Load();

    public void Load()
    {
        var items = new List<(WidgetDef, string)>();
        try
        {
            if (Directory.Exists(_folder))
                foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        if (WidgetRules.Parse(json, out _) is { } def) items.Add((def, json));
                    }
                    catch { /* skip unreadable files */ }
                }
        }
        catch { }
        _items = items;
    }

    public void Install(WidgetDef def, string json)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, def.Id + ".json"), json);
        Load();
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        try { File.Delete(Path.Combine(_folder, id + ".json")); } catch { }
        Load();
        Changed?.Invoke();
    }
}

/// <summary>Small HTTPS helper for installing widgets from a link or the community library.</summary>
public static class WidgetNet
{
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-Widgets");
        return c;
    }

    public static async Task<string> GetAsync(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Please use an https:// link.");
        using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > WidgetRules.MaxFileBytes) throw new InvalidOperationException("That file is larger than 64 KB.");
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > WidgetRules.MaxFileBytes) throw new InvalidOperationException("That file is larger than 64 KB.");
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
