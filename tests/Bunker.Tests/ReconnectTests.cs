using Bunker;
using Xunit;

public class ReconnectTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 20)]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(500, 60)]
    public void Waits_longer_each_time_then_settles_at_a_minute(int attempt, int seconds) => Assert.Equal(seconds, ReconnectPlan.DelaySeconds(attempt));

    [Fact]
    public void Never_waits_longer_than_a_minute()
    {
        for (var attempt = 1; attempt < 200; attempt++) Assert.InRange(ReconnectPlan.DelaySeconds(attempt), 1, ReconnectPlan.Longest);
    }

    [Fact]
    public void The_first_failures_are_logged_then_only_now_and_then()
    {
        Assert.True(ReconnectPlan.ShouldLogFailure(1));
        Assert.True(ReconnectPlan.ShouldLogFailure(3));
        Assert.False(ReconnectPlan.ShouldLogFailure(4));
        Assert.False(ReconnectPlan.ShouldLogFailure(9));
        Assert.True(ReconnectPlan.ShouldLogFailure(10));
        Assert.True(ReconnectPlan.ShouldLogFailure(20));
    }

    [Fact]
    public void Reconnecting_is_on_by_default() => Assert.True(new AppSettings().AutoReconnect);
}
