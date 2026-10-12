using Bunker;
using Xunit;

public class ConsoleFilterTests
{
    private static LogEntry Line(string text, LogKind kind = LogKind.Normal) => new(DateTime.Now, text, kind);
    private static HideRule Plugin(string name) => new() { Kind = HideRule.PluginKind, Value = name };
    private static HideRule Words(string text) => new() { Kind = HideRule.WordsKind, Value = text };

    [Fact]
    public void A_plugin_rule_hides_every_line_that_starts_with_that_plugin_in_brackets()
    {
        var rules = new List<HideRule> { Plugin("Economics"), Plugin("Steam Groups") };
        Assert.True(ConsoleFilter.IsHidden(rules, Line("[Economics] Saving balances for players...")));
        Assert.True(ConsoleFilter.IsHidden(rules, Line("[steam groups] Found 124 member(s) in chaosgami")));
        Assert.False(ConsoleFilter.IsHidden(rules, Line("[RustNav] Saving navmesh to path: server/rust/p")));
    }

    [Fact]
    public void A_plugin_rule_does_not_match_the_plugin_name_in_the_middle_of_a_line()
    {
        var rules = new List<HideRule> { Plugin("Economics") };
        Assert.False(ConsoleFilter.IsHidden(rules, Line("Reloading Economics now")));
        Assert.False(ConsoleFilter.IsHidden(rules, Line("[Better Chat] mentioned [Economics]")));
    }

    [Fact]
    public void A_words_rule_hides_any_line_containing_the_text_ignoring_capitals()
    {
        var rules = new List<HideRule> { Words("navmesh") };
        Assert.True(ConsoleFilter.IsHidden(rules, Line("[RustNav] Saving NavMesh to path: server/rust/p")));
        Assert.True(ConsoleFilter.IsHidden(rules, Line("navmesh rebuilt")));
        Assert.False(ConsoleFilter.IsHidden(rules, Line("[Better Chat] [Team] [ADMIN] Russ: Testing")));
    }

    [Fact]
    public void No_rules_hide_nothing() => Assert.False(ConsoleFilter.IsHidden(new List<HideRule>(), Line("anything")));

    [Fact]
    public void Your_own_commands_and_bunkers_notices_are_never_hidden()
    {
        var rules = new List<HideRule> { Words("say"), Plugin("say") };
        Assert.False(ConsoleFilter.IsHidden(rules, Line("> say hello", LogKind.Command)));
        Assert.False(ConsoleFilter.IsHidden(rules, Line("say is blocked", LogKind.Info)));
        Assert.True(ConsoleFilter.IsHidden(rules, Line("say something", LogKind.Normal)));
    }

    [Theory]
    [InlineData("[Economics] Saving balances", "Economics")]
    [InlineData("17:30:43  [Steam Groups] Found 124", "Steam Groups")]
    [InlineData("Barrel_Boss joined", null)]
    [InlineData("Result [x] later", null)]
    public void The_plugin_at_the_start_of_a_line_is_found(string line, string? expected) =>
        Assert.Equal(expected, ConsoleFilter.PluginOf(line));

    [Fact]
    public void Plugins_that_wrote_to_the_console_are_listed_busiest_first()
    {
        var entries = new[]
        {
            Line("[Economics] a"), Line("[Steam Groups] a"), Line("[Economics] b"), Line("[economics] c"),
            Line("plain line"), Line("[Oxide] hi", LogKind.Warning), Line("> [Cmd] mine", LogKind.Command),
        };
        var seen = ConsoleFilter.PluginsSeen(entries);
        Assert.Equal(("Economics", 3), seen[0]);
        Assert.Contains(("Steam Groups", 1), seen);
        Assert.Contains(("Oxide", 1), seen);
        Assert.DoesNotContain(seen, p => p.Name == "Cmd");
        Assert.DoesNotContain(ConsoleFilter.PluginsSeen(new[] { Line("[Error] Failed to call hook"), Line("[Warning] Slow tick") }), p => true); // level tags are not plugins
    }

    [Theory]
    [InlineData(HideRule.PluginKind, " [Steam Groups] ", "Steam Groups")]
    [InlineData(HideRule.PluginKind, "Economics", "Economics")]
    [InlineData(HideRule.WordsKind, "  [Steam Groups]\r\n ", "[Steam Groups]")]
    [InlineData(HideRule.WordsKind, "  \t ", "")]
    public void What_is_typed_is_tidied(string kind, string typed, string expected) => Assert.Equal(expected, ConsoleFilter.Clean(kind, typed));

    [Fact]
    public void Very_long_text_is_cut_to_a_sensible_length() =>
        Assert.Equal(ConsoleFilter.MaxLength, ConsoleFilter.Clean(HideRule.WordsKind, new string('x', 500)).Length);

    [Fact]
    public void Rules_read_clearly_in_the_list()
    {
        Assert.Equal("Plugin:  Economics", Plugin("Economics").ToString());
        Assert.Equal("Words:  navmesh", Words("navmesh").ToString());
    }
}
