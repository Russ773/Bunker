using Bunker;
using Xunit;

public class WatchTextTests
{
    [Theory]
    [InlineData("No connection could be made because the target machine actively refused it", "Connection refused: is RCON switched on, and is the port right?")]
    [InlineData("No such host is known.", "Host not found: check the address")]
    [InlineData("A connection attempt failed because the connected party did not properly respond after a period of time", "No answer (timed out)")]
    [InlineData("The server returned status code '401' when status code '101' was expected.", "Wrong RCON password")]
    [InlineData("", "Could not connect")]
    [InlineData(null, "Could not connect")]
    public void Errors_become_plain_language(string? message, string expected) => Assert.Equal(expected, WatchText.Describe(message));

    [Fact]
    public void An_unknown_error_is_shown_as_its_first_line_and_kept_short()
    {
        Assert.Equal("Something odd happened", WatchText.Describe("Something odd happened\nat System.Net.Sockets..."));
        var long_ = WatchText.Describe(new string('x', 300));
        Assert.True(long_.Length <= 90);
        Assert.EndsWith("…", long_);
    }

    [Theory]
    [InlineData(0, 0, 0, "No servers saved yet")]
    [InlineData(1, 1, 1, "1 server  ·  1 online  ·  1 player")]
    [InlineData(4, 3, 65, "4 servers  ·  3 online  ·  65 players")]
    [InlineData(3, 0, 0, "3 servers  ·  0 online")]
    public void Summary_line(int servers, int online, int players, string expected) => Assert.Equal(expected, WatchText.Summary(servers, online, players));

    [Fact]
    public void Server_names_read_as_a_sentence()
    {
        Assert.Equal("", WatchText.NameList(Array.Empty<string>()));
        Assert.Equal("A", WatchText.NameList(new[] { "A" }));
        Assert.Equal("A and B", WatchText.NameList(new[] { "A", "B" }));
        Assert.Equal("A, B and C", WatchText.NameList(new[] { "A", "B", "C" }));
    }

    [Fact]
    public void Player_line_shows_a_few_names_then_a_count()
    {
        Assert.Equal("", WatchText.PlayerLine(Array.Empty<string>()));
        Assert.Equal("Ann, Bob", WatchText.PlayerLine(new[] { "Ann", "Bob" }));
        Assert.Equal("Ann, Bob, Cy and 2 more", WatchText.PlayerLine(new[] { "Ann", "Bob", "Cy", "Di", "Ed" }));
        Assert.Equal("Ann", WatchText.PlayerLine(new[] { "Ann", "", "  " }));
    }

    [Fact]
    public void Retry_text_does_not_double_the_full_stop()
    {
        Assert.Equal("Host not found: check the address. Trying again in 5s", WatchText.WithRetry("Host not found: check the address", 5));
        Assert.Equal("Is the port right? Trying again in 10s", WatchText.WithRetry("Is the port right?", 10));
        Assert.Equal("Connection lost. Trying again in 3s", WatchText.WithRetry("Connection lost.", 3));
    }
}
