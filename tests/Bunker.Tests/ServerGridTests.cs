using System.Drawing;
using Bunker;
using Xunit;

public class ServerGridTests
{
    private static readonly string[] Saved = { "a", "b", "c", "d", "e", "f", "g", "h" };

    [Fact]
    public void Six_or_fewer_saved_servers_are_all_shown_whatever_was_chosen()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ServerGrid.Pick(new[] { "a", "b", "c" }, new[] { "a" }));
        Assert.Equal(6, ServerGrid.Pick(Saved.Take(6).ToList(), new List<string>()).Count);
    }

    [Fact]
    public void More_than_six_saved_shows_the_chosen_ones_in_saved_order()
    {
        Assert.Equal(new[] { "b", "d", "g" }, ServerGrid.Pick(Saved, new[] { "g", "b", "d" }));
    }

    [Fact]
    public void More_than_six_saved_with_nothing_chosen_shows_the_first_six()
    {
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, ServerGrid.Pick(Saved, new List<string>()));
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, ServerGrid.Pick(Saved, new[] { "deleted-server" }));
    }

    [Fact]
    public void Never_more_than_six_are_shown()
    {
        Assert.Equal(6, ServerGrid.Pick(Saved, Saved).Count);
    }

    [Theory]
    [InlineData(1, 1600, 1, 1)]
    [InlineData(2, 1600, 2, 1)]
    [InlineData(2, 600, 1, 2)]
    [InlineData(3, 1600, 3, 1)]
    [InlineData(3, 900, 2, 2)]
    [InlineData(4, 1600, 2, 2)]
    [InlineData(5, 1600, 3, 2)]
    [InlineData(6, 1600, 3, 2)]
    [InlineData(6, 900, 2, 3)]
    [InlineData(6, 500, 1, 6)]
    public void The_grid_suits_the_number_of_servers_and_the_window(int count, int width, int columns, int rows)
    {
        Assert.Equal((columns, rows), ServerGrid.Choose(count, width));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_server_gets_a_cell(int count)
    {
        for (var width = 300; width <= 2400; width += 100)
        {
            var (columns, rows) = ServerGrid.Choose(count, width);
            Assert.True(columns * rows >= count, $"{count} servers at {width} wide need {columns}x{rows}");
        }
    }

    [Fact]
    public void Cards_share_the_space_equally_but_never_get_too_small_to_read()
    {
        var min = new Size(340, 250);
        Assert.Equal(new Size(500, 400), ServerGrid.CardSize(3, 2, new Size(1528, 814), 14, min));
        Assert.Equal(min, ServerGrid.CardSize(3, 2, new Size(600, 300), 14, min));
    }
}
