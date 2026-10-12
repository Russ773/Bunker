using Bunker;
using SkiaSharp;
using Xunit;

public class ItemCatalogTests
{
    private const string Sample = """
    [
      { "id": 1, "shortname": "wood", "name": "Wood", "category": "Resources", "description": "A log.", "hidden": false },
      { "id": 2, "shortname": "rifle.ak", "name": "Assault Rifle", "category": "Weapon" },
      { "id": 3, "shortname": "woodtea.advanced", "name": "Advanced Wood Tea", "category": "Food" },
      { "id": 4, "shortname": "2module.car", "name": "2 Module Car", "category": "Misc", "hidden": true },
      { "id": 5, "shortname": "bad name!", "name": "Broken", "category": "Misc" },
      { "id": 6, "shortname": "wooden.door", "name": "Wooden Door", "category": "Construction" }
    ]
    """;

    private static ItemCatalog Catalog()
    {
        var c = new ItemCatalog(() => false);
        typeof(ItemCatalog).GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(c, ItemCatalog.Parse(Sample));
        return c;
    }

    [Fact]
    public void Hidden_items_and_unsafe_shortnames_are_left_out()
    {
        var items = ItemCatalog.Parse(Sample)!;
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, i => i.Shortname == "2module.car");
        Assert.DoesNotContain(items, i => i.Shortname.Contains(' '));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Bad_files_give_null(string json) => Assert.Null(ItemCatalog.Parse(json));

    [Fact]
    public void Searching_wood_puts_the_exact_item_first()
    {
        var results = Catalog().Search("wood");
        Assert.Equal("wood", results[0].Shortname);
        Assert.Contains(results, r => r.Shortname == "woodtea.advanced");
        Assert.Contains(results, r => r.Shortname == "wooden.door");
    }

    [Fact]
    public void Search_matches_names_shortnames_and_categories_ignoring_case()
    {
        var c = Catalog();
        Assert.Equal("rifle.ak", c.Search("ASSAULT")[0].Shortname);
        Assert.Equal("rifle.ak", c.Search("rifle.a")[0].Shortname);
        Assert.Equal("wooden.door", c.Search("construction")[0].Shortname);
        Assert.Empty(c.Search(""));
        Assert.Empty(c.Search("zzzz"));
    }

    [Fact]
    public void Find_is_by_exact_shortname()
    {
        var c = Catalog();
        Assert.Equal("Wood", c.Find("WOOD")?.Name);
        Assert.Null(c.Find("woo"));
    }

    [Fact]
    public void The_built_in_list_is_there_for_offline_use()
    {
        var offline = new ItemCatalog(() => false);
        Assert.True(offline.Items.Count > 500);
        Assert.NotNull(offline.Find("wood"));
    }

    [Fact]
    public void A_webp_picture_becomes_a_small_png()
    {
        var png = ItemCatalog.ToPng(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "wood.webp")));
        using var image = SKBitmap.Decode(png);
        Assert.Equal(64, image.Width);
        Assert.Equal(64, image.Height);
        Assert.Equal(0x89, png[0]); // PNG signature
    }

    [Fact]
    public void Something_that_is_not_a_webp_is_refused()
    {
        Assert.ThrowsAny<Exception>(() => ItemCatalog.ToPng(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }
}
