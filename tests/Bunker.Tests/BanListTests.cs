using Bunker;
using Xunit;

public class BanListTests
{
    // Made-up players, in the layout the server prints.
    private const string Sample =
        "1 76561198000000001 \"unnamed\" \"no reason\" -1\n" +
        "2 76561198000000002 \"Example Griefer\" \"Griefing other players' bases.\" -1\n" +
        "3 76561198000000003 \"Bad Sport\" \"Foul language and bad behaviour.\" 1893456000\n";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reads_every_ban()
    {
        var list = BanListParser.TryParse(Sample, Now)!;
        Assert.Equal(3, list.Rows.Count);
        Assert.Equal(new[] { "1", "76561198000000001", "unnamed", "no reason", "Permanent" }, list.Rows[0]);
        Assert.Equal("Example Griefer", list.Rows[1][2]);
        Assert.Equal("Griefing other players' bases.", list.Rows[1][3]);
        Assert.Equal(2, list.Permanent);
        Assert.Equal(1, list.Temporary);
    }

    [Fact]
    public void A_temporary_ban_shows_the_date_it_ends()
    {
        var list = BanListParser.TryParse(Sample, Now)!;
        Assert.Matches(@"^2030-01-0[1-2] \d\d:\d\d$", list.Rows[2][4]);
    }

    [Fact]
    public void A_ban_that_has_already_ended_says_so()
    {
        var past = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.EndsWith("(expired)", BanListParser.Expires(past, Now));
        Assert.Equal("Permanent", BanListParser.Expires(-1, Now));
    }

    [Fact]
    public void The_console_clock_in_front_of_the_first_line_is_ignored()
    {
        var list = BanListParser.TryParse("20:19:38  1 76561198000000001 \"a\" \"b\" -1\n2 76561198000000002 \"c\" \"d\" -1", Now)!;
        Assert.Equal(2, list.Rows.Count);
        Assert.Equal("76561198000000001", list.Rows[0][1]);
    }

    [Fact]
    public void Names_and_reasons_can_hold_quotes_and_spaces()
    {
        var list = BanListParser.TryParse("1 76561198000000001 \"The \"Boss\" Man\" \"said \"no\" to rules\" -1", Now)!;
        Assert.Single(list.Rows);
        Assert.Equal("76561198000000001", list.Rows[0][1]);
    }

    [Fact]
    public void Summary_counts_permanent_and_temporary_bans()
    {
        Assert.Equal("3 bans  ·  2 permanent  ·  1 temporary", BanListParser.TryParse(Sample, Now)!.Summary);
        Assert.Equal("1 ban  ·  1 permanent", BanListParser.TryParse("1 76561198000000001 \"a\" \"b\" -1", Now)!.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown command: banlistex")]
    [InlineData("1 76561198000000001 \"a\" \"b\" -1\nsomething else entirely")]
    [InlineData("status")]
    public void Anything_else_is_not_a_ban_list(string reply) => Assert.Null(BanListParser.TryParse(reply, Now));

    [Fact]
    public void A_ban_list_is_picked_up_by_the_reply_table_and_a_plugin_list_still_is()
    {
        Assert.IsType<BanList>(ReplyTables.TryParse(Sample));
        var plugins = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "carbon-plugins.txt"));
        Assert.IsType<PluginList>(ReplyTables.TryParse(plugins));
        Assert.Null(ReplyTables.TryParse("just some text"));
    }

    [Fact]
    public void Clicking_a_ban_row_picks_the_steam_id()
    {
        var list = (IReplyTable)BanListParser.TryParse(Sample, Now)!;
        Assert.Equal("Steam ID", list.Columns[list.PickColumn]);
        Assert.Equal("Name", list.Columns[list.BoldColumn]);
    }
}
