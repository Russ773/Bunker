using Bunker;
using Xunit;

public class TrayTests
{
    [Fact]
    public void Tooltip_names_the_server_and_status() => Assert.Equal("Bunker (Example PvE Server): Connected", TrayText.Tooltip("Example PvE Server", "Connected"));

    [Fact]
    public void Tooltip_without_a_server_is_just_the_status() => Assert.Equal("Bunker: Disconnected", TrayText.Tooltip("", "Disconnected"));

    [Fact]
    public void Tooltip_is_never_longer_than_windows_allows()
    {
        var text = TrayText.Tooltip(new string('S', 80), "Reconnecting in 30s (attempt 12)");
        Assert.True(text.Length <= TrayText.TooltipLimit);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Long_chat_is_trimmed_on_one_line()
    {
        var text = TrayText.Trim("first line\nsecond " + new string('x', 300), 50);
        Assert.Equal(50, text.Length);
        Assert.DoesNotContain('\n', text);
    }

    [Theory]
    [InlineData("cheat, hack", "this guy is using a CHEAT", true)]
    [InlineData("cheat, hack", "hello everyone", false)]
    [InlineData("", "anything", false)]
    [InlineData(null, "anything", false)]
    [InlineData("discord.gg;free scrap", "join discord.gg/abc", true)]
    [InlineData(" , ,", "anything", false)]
    public void Watch_words_match_whole_messages_ignoring_case(string? words, string message, bool expected) => Assert.Equal(expected, WatchWords.Matches(words, message));

    [Fact]
    public void Watch_words_split_on_commas_semicolons_and_new_lines() => Assert.Equal(new[] { "a", "b", "c d" }, WatchWords.Parse("a, b;c d\n"));

    [Fact]
    public void The_tray_icon_and_notifications_are_on_by_default_but_hiding_the_taskbar_button_is_not()
    {
        var s = new AppSettings();
        Assert.True(s.ShowTrayIcon);
        Assert.True(s.TrayNotifications);
        Assert.False(s.TrayHideWhenMinimised);
    }
}
