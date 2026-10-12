using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>Tidies chat text that chat plugins have already formatted, so it reads the way it does in the game.</summary>
public static class ChatText
{
    private static readonly Regex Markup = new(@"\x1B\[[0-9;]*[A-Za-z]|</?(?:color|size|b|i)(?:=[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Removes colour and size codes (they cannot be shown in the console) and control characters.</summary>
    public static string StripMarkup(string text) =>
        new string(Markup.Replace(text, "").Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();

    /// <summary>
    /// Chat plugins such as Better Chat put their own prefix and the player's name inside the message ("[ADMIN] Russ: Testing"), while the
    /// server also sends the name separately. Without this the app would show "Russ: [ADMIN] Russ: Testing". If the message starts with
    /// one or more [tags] followed by the player's own name and a colon, the tags are returned separately and the message is what is left.
    /// </summary>
    public static (string Tags, string Message) SplitTags(string username, string message)
    {
        if (string.IsNullOrWhiteSpace(username)) return ("", message);
        var m = Regex.Match(message, @"^(?<tags>(?:\[[^\]]*\]\s*)+)" + Regex.Escape(username.Trim()) + @":\s*(?<rest>.+)$",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups["tags"].Value.Trim(), m.Groups["rest"].Value.Trim()) : ("", message);
    }
}
