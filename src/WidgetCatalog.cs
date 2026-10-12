namespace Bunker;

/// <summary>Where a feature comes from, which decides how Bunker labels it.</summary>
public enum WidgetOrigin
{
    /// <summary>Part of the application itself: always there, not removable (Console, Players, ...).</summary>
    Core,
    /// <summary>A widget that ships with Bunker.</summary>
    BuiltIn,
    /// <summary>A widget the user installed from a JSON file.</summary>
    Community,
}

/// <summary>One entry in the list of things that can sit in a Layout section or open on the Widgets page.</summary>
public sealed record WidgetInfo(string Key, string Name, string Blurb, WidgetOrigin Origin, WidgetKind Kind, string? CustomId = null)
{
    public bool IsCommunity => Origin == WidgetOrigin.Community;
}

/// <summary>The one place that says which features are core and which are widgets, with a short description of each.</summary>
public static class WidgetCatalog
{
    /// <summary>The application's own panels. They are not widgets: they can be placed in a Layout section but never removed.</summary>
    public static readonly WidgetInfo[] CorePanels =
    {
        new("console", "Console", "Everything the server prints, and a box to run any command.", WidgetOrigin.Core, WidgetKind.Console),
        new("chat", "Chat", "The in-game chat on its own.", WidgetOrigin.Core, WidgetKind.Chat),
        new("players", "Players", "Who is online, with kick and ban on a right-click.", WidgetOrigin.Core, WidgetKind.Players),
        new("serverinfo", "Server info", "Players, FPS, entities, uptime, memory and map.", WidgetOrigin.Core, WidgetKind.ServerInfo),
    };

    /// <summary>The widgets that come with Bunker, in the order they are listed.</summary>
    public static readonly WidgetInfo[] BuiltIn =
    {
        new("announce", "Announce", "Broadcast a message to everyone, with saved presets.", WidgetOrigin.BuiltIn, WidgetKind.Announce),
        new("actions", "Quick actions", "One-click save, time and restart warnings.", WidgetOrigin.BuiltIn, WidgetKind.QuickActions),
        new("moderation", "Moderation", "Kick, ban, mute or unmute a player.", WidgetOrigin.BuiltIn, WidgetKind.Moderation),
        new("teleport", "Teleport", "Move a player to another player.", WidgetOrigin.BuiltIn, WidgetKind.Teleport),
        new("shortcuts", "Shortcuts", "Your own buttons for commands you run often.", WidgetOrigin.BuiltIn, WidgetKind.Shortcuts),
        new("give", "Give items", "Give a player an item or a stack.", WidgetOrigin.BuiltIn, WidgetKind.GiveItems),
        new("health", "Server health", "Live charts of FPS, players, entities and memory.", WidgetOrigin.BuiltIn, WidgetKind.Health),
        new("scheduler", "Scheduler", "Run commands on a timer: announcements, saves, restart sequences.", WidgetOrigin.BuiltIn, WidgetKind.Scheduler),
        new("playerdb", "Player database", "Everyone who has joined, with your own notes.", WidgetOrigin.BuiltIn, WidgetKind.PlayerDb),
        new("activity", "Join and leave feed", "A live feed of players joining and leaving.", WidgetOrigin.BuiltIn, WidgetKind.Activity),
        new("chatmon", "Chat monitor", "Watch-words, alerts and quick actions on chat messages.", WidgetOrigin.BuiltIn, WidgetKind.ChatMonitor),
    };

    public static WidgetInfo Community(WidgetDef d) =>
        new("w:" + d.Id, d.Name, string.IsNullOrWhiteSpace(d.Description) ? "A community widget." : d.Description!, WidgetOrigin.Community, WidgetKind.Custom, d.Id);

    /// <summary>Built-in widgets followed by the installed community widgets.</summary>
    public static IEnumerable<WidgetInfo> AllWidgets(Core core) =>
        BuiltIn.Concat(core.Widgets.All.Select(Community));

    /// <summary>Starred widgets first, keeping the usual order within each group.</summary>
    public static List<WidgetInfo> FavouritesFirst(IEnumerable<WidgetInfo> widgets, ICollection<string> favourites)
    {
        var all = widgets.ToList();
        return all.Where(w => favourites.Contains(w.Key)).Concat(all.Where(w => !favourites.Contains(w.Key))).ToList();
    }

    public static WidgetInfo? Find(WidgetKind kind) =>
        CorePanels.Concat(BuiltIn).FirstOrDefault(i => i.Kind == kind);

    /// <summary>"Core" for the application's own panels, "Built-in" and "Community" for widgets.</summary>
    public static string Label(WidgetOrigin origin) => origin switch
    {
        WidgetOrigin.Core => "Core",
        WidgetOrigin.BuiltIn => "Built-in",
        _ => "Community",
    };
}
