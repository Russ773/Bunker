namespace Bunker;

/// <summary>How long to wait between attempts to reconnect after the connection to the server drops.</summary>
public static class ReconnectPlan
{
    // A server that is restarting needs a minute or two, and one that is gone for the night should not be hammered: start quickly, then settle at a minute.
    private static readonly int[] Seconds = { 3, 5, 10, 20, 30 };
    public const int Longest = 60;

    /// <summary>Seconds to wait before attempt number <paramref name="attempt"/> (the first is 1).</summary>
    public static int DelaySeconds(int attempt) => attempt >= 1 && attempt <= Seconds.Length ? Seconds[attempt - 1] : Longest;

    /// <summary>Whether to write this failed attempt to the console. The first few are shown, then one now and then, so a night away does not fill the log.</summary>
    public static bool ShouldLogFailure(int attempt) => attempt <= 3 || attempt % 10 == 0;
}
