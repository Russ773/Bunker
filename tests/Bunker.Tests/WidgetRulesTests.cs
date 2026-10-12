using Bunker;
using Xunit;

public class WidgetRulesTests
{
    private static string Widget(string controls, string id = "test-widget") =>
        $"{{ \"id\": \"{id}\", \"name\": \"Test\", \"controls\": [ {controls} ] }}";

    private static List<string> Errors(string json) { WidgetRules.Parse(json, out var errors); return errors; }

    [Fact]
    public void Every_bundled_widget_is_valid()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "widgets");
        var files = Directory.GetFiles(folder, "*.json").Where(f => Path.GetFileName(f) != "index.json").ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var def = WidgetRules.Parse(File.ReadAllText(file), out var errors);
            Assert.True(def is not null, $"{Path.GetFileName(file)}: {string.Join("; ", errors)}");
            Assert.Equal(Path.GetFileNameWithoutExtension(file), def!.Id);
        }
    }

    [Theory]
    [InlineData("rcon.password hunter2")]
    [InlineData("rcon.web 0")]
    [InlineData("ownerid 76561198000000001 admin")]
    [InlineData("moderatorid 76561198000000001 mod")]
    [InlineData("removeowner 76561198000000001")]
    [InlineData("OWNERID 76561198000000001")]
    public void Admin_and_rcon_commands_are_refused(string command)
    {
        var errors = Errors(Widget($"{{ \"type\": \"button\", \"text\": \"Go\", \"commands\": [ \"{command}\" ] }}"));
        Assert.Contains(errors, e => e.Contains("not allowed"));
    }

    [Theory]
    [InlineData("quit", CommandRisk.Warning)]
    [InlineData("restart 300", CommandRisk.Warning)]
    [InlineData("inventory.giveall wood 10", CommandRisk.Warning)]
    [InlineData("oxide.unload SomePlugin", CommandRisk.Warning)]
    [InlineData("say hello", CommandRisk.Normal)]
    [InlineData("inventory.giveto 765 wood 10", CommandRisk.Normal)]
    [InlineData("rcon.password x", CommandRisk.Blocked)]
    public void Commands_are_graded(string command, CommandRisk expected) =>
        Assert.Equal(expected, WidgetRules.RiskOf(command));

    [Fact]
    public void Placeholders_must_match_a_field()
    {
        var errors = Errors(Widget("{ \"type\": \"button\", \"text\": \"Go\", \"commands\": [ \"say {nothing}\" ] }"));
        Assert.Contains(errors, e => e.Contains("does not match any field"));
    }

    [Fact]
    public void Name_placeholder_only_works_for_players()
    {
        var errors = Errors(Widget("{ \"type\": \"text\", \"id\": \"msg\" }, { \"type\": \"button\", \"text\": \"Go\", \"commands\": [ \"say {msg.name}\" ] }"));
        Assert.Contains(errors, e => e.Contains("only works for player"));
    }

    [Theory]
    [InlineData("UPPER")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("../escape")]
    public void Bad_ids_are_refused(string id) =>
        Assert.NotEmpty(Errors(Widget("{ \"type\": \"label\", \"text\": \"hi\" }", id)));

    [Fact]
    public void Unknown_control_types_are_refused() =>
        Assert.Contains(Errors(Widget("{ \"type\": \"script\", \"text\": \"x\" }")), e => e.Contains("Unknown control type"));

    [Fact]
    public void Files_over_64KB_are_refused()
    {
        var big = Widget("{ \"type\": \"label\", \"text\": \"" + new string('x', 70_000) + "\" }");
        Assert.Contains(Errors(big), e => e.Contains("64 KB"));
    }

    [Fact]
    public void Broken_json_is_reported_not_thrown() =>
        Assert.NotEmpty(Errors("{ this is not json"));

    [Fact]
    public void Clean_removes_anything_that_could_escape_an_argument()
    {
        Assert.Equal("hi there", WidgetRules.Clean("  hi \"there\";\\  "));
        Assert.Equal("ab", WidgetRules.Clean("a\r\nb"));
        Assert.Equal(200, WidgetRules.Clean(new string('x', 500)).Length);
    }

    [Fact]
    public void Expand_fills_values_and_player_names()
    {
        var values = new Dictionary<string, string> { ["target"] = "76561198000000001", ["amount"] = "50" };
        var names = new Dictionary<string, string> { ["target"] = "Ironclad" };
        Assert.Equal("inventory.giveto 76561198000000001 wood 50", WidgetRules.Expand("inventory.giveto {target} wood {amount}", values, names));
        Assert.Equal("Give Ironclad a pack?", WidgetRules.Expand("Give {target.name} a pack?", values, names));
    }
}

public class WidgetLibraryIndexTests
{
    [Fact]
    public void Index_lists_exactly_the_widget_files()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "widgets");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "index.json")));
        var listed = doc.RootElement.GetProperty("widgets").EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToList();
        var onDisk = Directory.GetFiles(folder, "*.json").Select(Path.GetFileName).Where(n => n != "index.json").OrderBy(n => n).ToList();
        Assert.Equal(onDisk, listed);
    }
}
