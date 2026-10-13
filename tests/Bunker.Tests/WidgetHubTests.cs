using Bunker;
using Xunit;

public class WidgetHubTests
{
    private static WidgetInfo B(string key, string name = "", string blurb = "") => new(key, name.Length > 0 ? name : key, blurb, WidgetOrigin.BuiltIn, WidgetKind.Announce);
    private static WidgetInfo C(string key, string name = "", string blurb = "") => new("w:" + key, name.Length > 0 ? name : key, blurb, WidgetOrigin.Community, WidgetKind.Custom, key);

    private static readonly WidgetInfo[] All = { B("announce"), B("health"), B("scheduler"), C("ban"), C("countdown") };

    private static string[] Keys(WidgetGroup g) => g.Widgets.Select(w => w.Key).ToArray();

    [Fact]
    public void Widgets_are_grouped_by_where_they_came_from()
    {
        var groups = WidgetGroups.Build(All, new List<string>(), new List<string>(), null);
        Assert.Equal(new[] { WidgetGroups.BuiltIn, WidgetGroups.Community }, groups.Select(g => g.Title));
        Assert.Equal(new[] { "announce", "health", "scheduler" }, Keys(groups[0]));
        Assert.Equal(new[] { "w:ban", "w:countdown" }, Keys(groups[1]));
    }

    [Fact]
    public void A_favourite_is_listed_once_in_the_order_it_was_starred()
    {
        var groups = WidgetGroups.Build(All, new List<string> { "w:ban", "announce" }, new List<string>(), null);
        Assert.Equal(WidgetGroups.Favourites, groups[0].Title);
        Assert.Equal(new[] { "w:ban", "announce" }, Keys(groups[0]));
        Assert.DoesNotContain("announce", Keys(groups.First(g => g.Title == WidgetGroups.BuiltIn)));
        Assert.DoesNotContain(groups.Skip(1), g => Keys(g).Contains("w:ban"));
    }

    [Fact]
    public void A_hidden_widget_is_only_in_the_hidden_group_even_if_starred()
    {
        var groups = WidgetGroups.Build(All, new List<string> { "health" }, new List<string> { "health" }, null);
        Assert.DoesNotContain(groups, g => g.Title == WidgetGroups.Favourites);
        var hidden = groups.Single(g => g.Title == WidgetGroups.Hidden);
        Assert.Equal(new[] { "health" }, Keys(hidden));
        Assert.True(hidden.Dim);
        Assert.Equal(1, groups.Count(g => g.Widgets.Any(w => w.Key == "health")));
    }

    [Fact]
    public void Empty_groups_are_left_out() =>
        Assert.Equal(new[] { WidgetGroups.BuiltIn }, WidgetGroups.Build(new[] { B("a"), B("b") }, new List<string>(), new List<string>(), null).Select(g => g.Title));

    [Fact]
    public void A_star_for_a_widget_that_no_longer_exists_is_ignored()
    {
        var groups = WidgetGroups.Build(All, new List<string> { "w:removed", "announce" }, new List<string>(), null);
        Assert.Equal(new[] { "announce" }, Keys(groups[0]));
    }

    [Fact]
    public void Search_finds_matches_in_names_and_descriptions_including_hidden_widgets()
    {
        var widgets = new[] { B("a", "Announce", "Broadcast a message"), B("m", "Moderation", "Kick, ban or mute"), C("ban", "Ban Manager", "See the ban list") };
        var groups = WidgetGroups.Build(widgets, new List<string> { "a" }, new List<string> { "w:ban" }, "ban");
        Assert.Single(groups);
        Assert.Equal("Results (2)", groups[0].Title);
        Assert.Equal(new[] { "m", "w:ban" }, Keys(groups[0]));
    }

    [Fact]
    public void Search_is_not_case_sensitive_and_says_so_when_nothing_matches()
    {
        Assert.Equal(1, WidgetGroups.Build(new[] { B("a", "Announce") }, new List<string>(), new List<string>(), "  ANNOUNCE ")[0].Widgets.Count);
        Assert.Equal("Results (0)", WidgetGroups.Build(All, new List<string>(), new List<string>(), "zzz")[0].Title);
    }

    [Fact]
    public void The_sidebar_lists_favourites_in_the_order_starred()
    {
        var rows = WidgetGroups.Sidebar(All, new List<string> { "scheduler", "announce" }, new List<string>(), null);
        Assert.Equal(new[] { "scheduler", "announce" }, rows.Select(w => w.Key));
    }

    [Fact]
    public void The_sidebar_also_shows_the_widget_that_is_open_when_it_is_not_a_favourite()
    {
        var rows = WidgetGroups.Sidebar(All, new List<string> { "announce" }, new List<string>(), "w:ban");
        Assert.Equal(new[] { "announce", "w:ban" }, rows.Select(w => w.Key));
        // an open favourite is not listed twice
        Assert.Single(WidgetGroups.Sidebar(All, new List<string> { "announce" }, new List<string>(), "announce"));
    }

    [Fact]
    public void The_sidebar_skips_hidden_favourites_and_unknown_keys()
    {
        var rows = WidgetGroups.Sidebar(All, new List<string> { "health", "w:gone", "announce" }, new List<string> { "health" }, "nothing");
        Assert.Equal(new[] { "announce" }, rows.Select(w => w.Key));
    }

    [Fact]
    public void Hiding_a_widget_also_un_stars_it_and_showing_it_again_brings_it_back_to_the_groups()
    {
        var favourites = new List<string> { "health", "announce" };
        var hidden = new List<string>();
        WidgetGroups.SetHidden(favourites, hidden, "health", true);
        WidgetGroups.SetHidden(favourites, hidden, "health", true); // twice does no harm
        Assert.Equal(new[] { "health" }, hidden);
        Assert.Equal(new[] { "announce" }, favourites);
        WidgetGroups.SetHidden(favourites, hidden, "health", false);
        Assert.Empty(hidden);
    }
}
