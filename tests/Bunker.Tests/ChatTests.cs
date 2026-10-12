using System.Drawing;
using Bunker;
using Xunit;

public class ChatTests
{
    [Fact]
    public void Chat_json_gives_name_message_colour_and_channel()
    {
        var (text, userId, name, message, color, channel) = Core.ParseChat("{\"Channel\":1,\"Message\":\"on my way\",\"UserId\":\"76561198000000001\",\"Username\":\"Ironclad\",\"Color\":\"#5af\",\"Time\":1700000000}");
        Assert.Equal("Ironclad: on my way", text);
        Assert.Equal("76561198000000001", userId);
        Assert.Equal("Ironclad", name);
        Assert.Equal("on my way", message);
        Assert.Equal("#5af", color);
        Assert.Equal(1, channel);
    }

    [Fact]
    public void Chat_without_colour_or_channel_still_works()
    {
        var (_, _, name, _, color, channel) = Core.ParseChat("{\"Username\":\"SERVER\",\"Message\":\"hello\"}");
        Assert.Equal("SERVER", name);
        Assert.Null(color);
        Assert.Equal(0, channel);
    }

    [Fact]
    public void Plain_text_is_not_treated_as_chat()
    {
        var (text, userId, name, _, _, _) = Core.ParseChat("just a line");
        Assert.Equal("just a line", text);
        Assert.Null(userId);
        Assert.Null(name);
    }

    [Theory]
    [InlineData("#5af", 0x55, 0xAA, 0xFF)]
    [InlineData("#55aaff", 0x55, 0xAA, 0xFF)]
    [InlineData("af5", 0xAA, 0xFF, 0x55)]
    public void Hex_colours_are_read(string text, int r, int g, int b)
    {
        var c = Theme.ParseHex(text);
        Assert.Equal(Color.FromArgb(r, g, b).ToArgb(), c!.Value.ToArgb());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12")]
    public void Not_a_colour_gives_null(string? text) => Assert.Null(Theme.ParseHex(text));

    [Fact]
    public void Very_dark_names_are_lightened_so_they_can_be_read_on_the_dark_theme()
    {
        var shown = Theme.Readable(Color.FromArgb(10, 10, 40));
        Assert.True(shown.R + shown.G + shown.B > 10 + 10 + 40 + 100);
        Assert.Equal(Color.FromArgb(0x55, 0xAA, 0xFF).ToArgb(), Theme.Readable(Color.FromArgb(0x55, 0xAA, 0xFF)).ToArgb()); // already readable
    }
}
