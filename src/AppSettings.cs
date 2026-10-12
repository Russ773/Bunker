using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bunker;

public class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 28016;

    /// <summary>The RCON password, encrypted with Windows DPAPI (only this Windows user can decrypt it).</summary>
    public string? PasswordProtected { get; set; }

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordProtected)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => PasswordProtected = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetKind { Console, Chat, Players, ServerInfo, GiveItems, Announce, QuickActions, Moderation, Teleport, Shortcuts }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutPreset { Single, TwoColumns, TwoRows, ThreeLeftBig, FourGrid }

/// <summary>The user's custom view: a preset arrangement plus which widget fills each slot.</summary>
public class LayoutConfig
{
    public LayoutPreset Preset { get; set; } = LayoutPreset.FourGrid;
    public WidgetKind[] Slots { get; set; } = { WidgetKind.Players, WidgetKind.Console, WidgetKind.ServerInfo, WidgetKind.Chat };

    [JsonIgnore]
    public int SlotCount => Preset switch
    {
        LayoutPreset.Single => 1,
        LayoutPreset.TwoColumns or LayoutPreset.TwoRows => 2,
        LayoutPreset.ThreeLeftBig => 3,
        _ => 4,
    };

    public WidgetKind SlotAt(int i) => Slots is { Length: > 0 } && i < Slots.Length ? Slots[i] : WidgetKind.Console;
}

/// <summary>A user-defined button that runs a console command.</summary>
public class CommandShortcut
{
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
    public bool Confirm { get; set; }
}

public class AppSettings
{
    public bool CheckForUpdatesOnLaunch { get; set; } = true;
    public bool StartMaximised { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public List<ServerProfile> Servers { get; set; } = new();
    public string? ActiveServerId { get; set; }
    public LayoutConfig Layout { get; set; } = new();
    public List<string> SavedAnnouncements { get; set; } = new();
    public List<CommandShortcut> Shortcuts { get; set; } = new();

    /// <summary>The single server saved by older versions. Moved into Servers the first time settings load.</summary>
    [JsonPropertyName("Server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerProfile? LegacyServer { get; set; }

    [JsonIgnore]
    public ServerProfile? ActiveServer => Servers.FirstOrDefault(s => s.Id == ActiveServerId) ?? Servers.FirstOrDefault();

    public void AddOrUpdateServer(ServerProfile profile, bool makeActive)
    {
        var i = Servers.FindIndex(s => s.Id == profile.Id);
        if (i >= 0) Servers[i] = profile; else Servers.Add(profile);
        if (makeActive || ActiveServerId is null) ActiveServerId = profile.Id;
    }

    public void RemoveServer(string id)
    {
        Servers.RemoveAll(s => s.Id == id);
        if (ActiveServerId == id) ActiveServerId = Servers.FirstOrDefault()?.Id;
    }

    // BUNKER_DATA_DIR lets tests run against a throwaway settings folder instead of the real one.
    private static readonly string FilePath = Path.Combine(Environment.GetEnvironmentVariable("BUNKER_DATA_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bunker"), "settings.json");

    public static string Folder => Path.GetDirectoryName(FilePath)!;

    public static AppSettings Load()
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }

        if (s.LegacyServer is not null)
        {
            if (s.Servers.Count == 0) s.Servers.Add(s.LegacyServer);
            s.ActiveServerId ??= s.LegacyServer.Id;
            s.LegacyServer = null;
            try { s.Save(); } catch { }
        }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
