using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bunker;

public class ServerProfile
{
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
public enum WidgetKind { Console, Chat, Players, ServerInfo }

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

public class AppSettings
{
    public bool CheckForUpdatesOnLaunch { get; set; } = true;
    public ServerProfile? Server { get; set; }
    public LayoutConfig Layout { get; set; } = new();

    // BUNKER_DATA_DIR lets tests run against a throwaway settings folder instead of the real one.
    private static readonly string FilePath = Path.Combine(Environment.GetEnvironmentVariable("BUNKER_DATA_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bunker"), "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
