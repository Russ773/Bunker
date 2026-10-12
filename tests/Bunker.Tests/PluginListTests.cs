using Bunker;
using Xunit;

public class PluginListTests
{
    private static PluginList Carbon()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "carbon-plugins.txt"));
        return Assert.IsType<PluginList>(PluginListParser.TryParse(text));
    }

    [Fact]
    public void Carbon_table_is_read_into_columns()
    {
        var list = Carbon();
        Assert.Equal("Carbon", list.Framework);
        Assert.Equal(new[] { "Type", "Plugin", "Author", "Version", "Hook time", "Hook fires", "Memory", "Compile time", "Uptime" }, list.Columns);
        Assert.Equal(12, list.Rows.Count); // 1 core + 9 scripts + 2 extensions
    }

    [Fact]
    public void Each_value_lands_under_its_own_heading()
    {
        var list = Carbon();
        string[] Row(string name) => list.Rows.Single(r => r[list.NameColumn] == name);

        // "737ms [424ms]" sits under "compile time" in Carbon's own table, not under "hook lag".
        Assert.Equal(new[] { "Scripts", "AFK API", "2CHEVSKII", "v1.0.1", "23ms", "12,301", "40.0kb", "737ms [424ms]", "11h50m30s" }, Row("AFK API"));
        Assert.Equal("Wulf/lukespragg/Arainrr/James/Bushhy", Row("Auto Doors")[2]);
        Assert.Equal("73790ms", Row("Neutral NPCs")[4]);
        Assert.Equal("1m45s", Row("Steam Groups")[8]);
    }

    [Fact]
    public void Empty_cells_stay_empty_and_do_not_shift_the_row()
    {
        var list = Carbon();
        var checker = list.Rows.Single(r => r[1] == "ChaosUpdateChecker");
        Assert.Equal("", checker[4]);          // no hook time
        Assert.Equal("2", checker[5]);         // fires
        var spam = list.Rows.Single(r => r[1] == "Console Spam Cleaner");
        Assert.Equal("", spam[6]);             // no memory
        Assert.Equal("80ms [5ms]", spam[7]);
    }

    [Fact]
    public void Groups_become_the_type_without_their_count()
    {
        var list = Carbon();
        Assert.Equal("Carbon Community", list.Rows.Single(r => r[1] == "Core")[0]);
        Assert.Equal("Chaos - k1lly0u (Oxide Extension)", list.Rows.Single(r => r[1] == "Chaos Commands")[0]);
        Assert.DoesNotContain(list.Rows, r => r[0].Contains("(1"));
    }

    [Fact]
    public void Unloaded_and_failed_plugins_are_reported_separately()
    {
        var list = Carbon();
        Assert.Equal(new[] { "PurgeFX.cs" }, list.Unloaded);
        Assert.Equal(3, list.Failed.Count);
        Assert.Equal("AutoRestart.cs", list.Failed[0].File);
        Assert.Equal("16:17", list.Failed[0].Line);
        Assert.StartsWith("The type or namespace name 'CrashExtensions'", list.Failed[0].Message);
        Assert.Equal("The name 'CUIControls' does not exist in the current context", list.Failed[2].Message);
        Assert.Contains("1 failed to compile", list.Summary);
        Assert.Contains("12 plugins loaded", list.Summary);
    }

    [Fact]
    public void Oxide_list_is_read_into_columns()
    {
        var reply = "Listing 2 plugins:\n  01 \"Better Chat\" (5.2.15) by LaserHydra (0.29s / 10 KiB) - BetterChat.cs\n  02 \"Welcomer\" (2.1.4) by Bunker (0.01s / 2 KiB) - Welcomer.cs\n";
        var list = Assert.IsType<PluginList>(PluginListParser.TryParse(reply));
        Assert.Equal("Oxide", list.Framework);
        Assert.Equal(new[] { "Better Chat", "5.2.15", "LaserHydra", "0.29s", "10 KiB", "BetterChat.cs" }, list.Rows[0]);
        Assert.Equal(2, list.Rows.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown command: c.plugins")]
    [InlineData("executed: status")]
    public void Anything_else_is_not_a_plugin_list(string reply) => Assert.Null(PluginListParser.TryParse(reply));

    [Theory]
    [InlineData("9ms", "1092ms")]
    [InlineData("40.0kb", "4.2mb")]
    [InlineData("1m45s", "11h50m31s")]
    [InlineData("2", "12,301")]
    [InlineData("v1.9.0", "v1.10.0")]
    [InlineData("0.29s", "1.5s")]
    public void Values_sort_by_size_not_by_letters(string smaller, string larger)
    {
        Assert.True(PluginListParser.SortValue(smaller) < PluginListParser.SortValue(larger), $"{smaller} should sort before {larger}");
    }

    [Fact]
    public void Text_has_no_number_to_sort_by()
    {
        Assert.Null(PluginListParser.SortValue("Wulf/lukespragg"));
        Assert.Null(PluginListParser.SortValue(""));
    }
}
