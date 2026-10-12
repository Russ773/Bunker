using Bunker;
using Xunit;

public class ChatSenderTests
{
    [Fact]
    public void The_plugin_command_carries_steam_id_name_colour_and_message()
    {
        Assert.Equal("bunker.say 76561198000000001 \"Russ\" \"#af5\" \"Hello everyone\"",
            ChatSender.PluginCommand("76561198000000001", "Russ", "#af5", "Hello everyone"));
    }

    [Fact]
    public void Quotes_and_control_characters_cannot_break_out_of_the_command()
    {
        var command = ChatSender.PluginCommand("76561198000000001", "Ru\"ss", "#af5", "hi\" ; quit \"x\r\n");
        Assert.DoesNotContain("\n", command);
        Assert.DoesNotContain("\r", command);
        Assert.Equal(6, command.Count(c => c == (char)34)); // only the three pairs we put there (name, colour, message)
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("7656119800000000X")]
    public void A_missing_or_bad_steam_id_is_sent_as_zero(string id)
    {
        Assert.StartsWith("bunker.say 0 ", ChatSender.PluginCommand(id, "Russ", "#af5", "hi"));
    }

    [Theory]
    [InlineData("#5af", "#5af")]
    [InlineData("af5", "#af5")]
    [InlineData("  #FA5 ", "#FA5")]
    [InlineData("green", "#af5")]
    [InlineData("", "#af5")]
    [InlineData(null, "#af5")]
    public void Colours_are_tidied_or_replaced_by_the_admin_green(string? input, string expected) =>
        Assert.Equal(expected, ChatSender.ColourOrDefault(input));

    [Theory]
    [InlineData("#fa5", "#fa5")]
    [InlineData("5af", "#5af")]
    [InlineData("nope", "#5af")]
    [InlineData(null, "#5af")]
    public void Plugin_prefix_colour_defaults_to_blue(string? input, string expected) =>
        Assert.Equal(expected, ChatSender.TagColourOrDefault(input));

    [Fact]
    public void Plain_say_adds_your_name_only_when_you_have_one()
    {
        Assert.Equal("say hello", ChatSender.SayCommand("", "hello"));
        Assert.Equal("say \"Russ: hello\"", ChatSender.SayCommand(" Russ ", "hello"));
    }

    [Theory]
    [InlineData("Unknown command: bunker.say", true)]
    [InlineData("Command 'bunker.say' not found", true)]
    [InlineData("sent", false)]
    [InlineData("Admins only.", false)]
    [InlineData("", false)]
    public void An_unknown_command_reply_means_the_plugin_is_not_installed(string reply, bool unknown) =>
        Assert.Equal(unknown, ChatSender.LooksUnknown(reply));

    [Theory]
    [InlineData("76561198000000001", true)]
    [InlineData("7656119800000000", false)]
    [InlineData("765611980000000012", false)]
    [InlineData("", false)]
    public void A_steam_id_is_exactly_seventeen_digits(string id, bool valid) => Assert.Equal(valid, ChatSender.IsSteamId(id));

    [Fact]
    public void The_plugin_is_built_into_the_app_so_it_can_be_copied()
    {
        var source = ChatSender.PluginSource();
        Assert.Contains("bunker.say", source);
        Assert.Contains("chat.add2", source);
        Assert.Contains("authLevel < 2", source); // admin only
    }
}
