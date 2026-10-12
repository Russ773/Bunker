namespace Bunker;

/// <summary>
/// Tells whether something typed in the console box looks like a server command, or just a message. Bunker uses it to ask
/// "say this in chat?" instead of sending ordinary words to the server as a command that does nothing.
/// </summary>
public static class CommandGuess
{
    // Rust's commands that are a single word. Everything else the server understands is written with a dot (server.save, c.reload,
    // oxide.reload, inventory.giveto), which is how a command can be told from a sentence.
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "players", "playerlist", "serverinfo", "kick", "kickall", "ban", "banid", "unban", "banlist", "banlistex", "listid", "listip",
        "mute", "unmute", "say", "find", "findtext", "help", "quit", "restart", "save", "writecfg", "readcfg", "count", "version", "echo", "clear",
        "pause", "bind", "teleport", "teleport2me", "teleportany", "teleportpos", "ownerid", "moderatorid", "removeowner", "removemoderator",
        "kill", "respawn", "spawn", "give", "giveid", "givearm", "giveto", "entid", "ent", "sleepingusers", "perf", "debug", "console",
    };

    /// <summary>
    /// True if the first word is a known command or has a dot in it (a namespaced command). Words typed earlier and confirmed as
    /// commands (<paramref name="confirmed"/>) count too, so Bunker does not ask about the same plugin command twice.
    /// </summary>
    public static bool LooksLikeCommand(string text, ISet<string>? confirmed = null)
    {
        var t = text.Trim();
        if (t.Length == 0) return true;
        var first = t.Split(' ', 2)[0];
        if (first.Contains('.') || Known.Contains(first)) return true;
        return confirmed is not null && confirmed.Contains(first);
    }

    public static string FirstWord(string text) => text.Trim().Split(' ', 2)[0];
}
