using Bunker;
using Xunit;

public class CloseButtonTests
{
    [Theory]
    [InlineData("ask", "ask")]
    [InlineData("minimise", "minimise")]
    [InlineData("close", "close")]
    [InlineData(null, "ask")]
    [InlineData("", "ask")]
    [InlineData("Minimize", "ask")] // anything unrecognised means ask
    public void Setting_is_normalised(string? stored, string expected) => Assert.Equal(expected, CloseButtonSetting.Normalise(stored));

    [Fact]
    public void A_new_install_asks() => Assert.Equal(CloseButtonSetting.Ask, CloseButtonSetting.Normalise(new AppSettings().CloseButton));
}
