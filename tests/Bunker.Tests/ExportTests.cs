using Bunker;
using Xunit;

public class ExportTests
{
    private static readonly DateTime T = new(2026, 10, 13, 19, 25, 2);

    [Fact]
    public void A_console_line_has_the_date_and_the_text()
    {
        Assert.Equal("2026-10-13 19:25:02  Server started.", ExportText.LogLine(new LogEntry(T, "Server started.", LogKind.Normal)));
    }

    [Fact]
    public void Chat_is_written_the_way_it_looks_in_the_game()
    {
        var team = new LogEntry(T, "x", LogKind.Chat, "765", "Ironclad", "need a hand?", "#5af", 1);
        Assert.Equal("2026-10-13 19:25:02  [TEAM] Ironclad: need a hand?", ExportText.LogLine(team));
        var tagged = new LogEntry(T, "x", LogKind.Chat, "765", "AdminRuss", "Restart soon", "#af5", 0, "[ADMIN]");
        Assert.Equal("2026-10-13 19:25:02  [ADMIN] AdminRuss: Restart soon", ExportText.LogLine(tagged));
    }

    [Fact]
    public void The_log_is_one_line_per_entry()
    {
        var text = ExportText.LogText(new[] { new LogEntry(T, "a", LogKind.Normal), new LogEntry(T, "b", LogKind.Warning) });
        Assert.Equal(2, text.TrimEnd().Split('\n').Length);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Cells_are_quoted_when_they_need_to_be(string? value, string expected) => Assert.Equal(expected, ExportText.Cell(value));

    [Theory]
    [InlineData("=SUM(A1:A9)")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@cmd")]
    public void A_name_that_a_spreadsheet_would_run_as_a_formula_is_made_harmless(string value) => Assert.StartsWith("'", ExportText.Cell(value));

    [Fact]
    public void The_player_csv_has_a_heading_and_a_row_per_player()
    {
        var p = new PlayerRecord { SteamId = "76561198000000001", Name = "Iron, clad", FirstSeenUtc = T.ToUniversalTime(), LastSeenUtc = T.ToUniversalTime(), TotalSeconds = 5400, Sessions = 3, Note = "likes \"rockets\"" };
        var csv = ExportText.PlayersCsv(new[] { p }, id => id == p.SteamId).TrimEnd().Split('\n');
        Assert.Equal("Steam ID,Name,First seen,Last seen,Hours played,Visits,Online now,Note,Steam profile", csv[0].Trim());
        Assert.Equal(2, csv.Length);
        Assert.StartsWith("=\"76561198000000001\",\"Iron, clad\",", csv[1]);
        Assert.Contains(",1.50,3,Yes,", csv[1]);
        Assert.Contains("\"likes \"\"rockets\"\"\"", csv[1]);
        Assert.EndsWith("https://steamcommunity.com/profiles/76561198000000001", csv[1].Trim());
    }

    [Fact]
    public void File_names_are_safe_and_carry_the_time()
    {
        Assert.Equal("bunker-console-2026-10-13-1925.txt", ExportText.FileName("bunker-console", T, "txt"));
        Assert.Equal("players-Example-PvE-Server-2026-10-13-1925.csv", ExportText.FileName("players-Example PvE Server", T, ".csv"));
        Assert.Equal("players-a-b-c-2026-10-13-1925.csv", ExportText.FileName("players-a/b:c", T, "csv"));
        Assert.Equal("bunker-2026-10-13-1925.txt", ExportText.FileName("   ", T, "txt"));
    }
}

public class WhatsNewTests
{
    private static readonly WhatsNewEntry[] Entries =
    {
        new("1.0.3", new[] { "c" }), new("1.0.1", new[] { "a" }), new("1.0.2", new[] { "b" }), new("1.0.9", new[] { "future" }),
    };

    [Fact]
    public void Shows_what_is_newer_than_the_last_version_seen_newest_first()
    {
        var notes = WhatsNew.Since(Entries, new Version(1, 0, 1), new Version(1, 0, 3));
        Assert.Equal(new[] { "1.0.3", "1.0.2" }, notes.Select(n => n.Version));
    }

    [Fact]
    public void Never_shows_notes_for_a_version_that_is_not_out_yet() =>
        Assert.DoesNotContain(WhatsNew.Since(Entries, new Version(0, 0, 0), new Version(1, 0, 3)), n => n.Version == "1.0.9");

    [Fact]
    public void Nothing_is_shown_when_nothing_changed_or_after_a_downgrade()
    {
        Assert.Empty(WhatsNew.Since(Entries, new Version(1, 0, 3), new Version(1, 0, 3)));
        Assert.Empty(WhatsNew.Since(Entries, new Version(1, 0, 3), new Version(1, 0, 1)));
    }

    [Fact]
    public void The_list_is_capped()
    {
        var many = Enumerable.Range(1, 12).Select(i => new WhatsNewEntry($"1.0.{i}", new[] { "x" })).ToList();
        Assert.Equal(5, WhatsNew.Since(many, new Version(0, 0, 0), new Version(1, 0, 12)).Count);
        Assert.Equal(3, WhatsNew.Latest(many, new Version(1, 0, 12), 3).Count);
    }

    [Fact]
    public void Entries_without_a_valid_version_or_any_items_are_ignored()
    {
        var parsed = WhatsNew.Parse("[{\"version\":\"1.0.1\",\"items\":[\"ok\"]},{\"version\":\"nope\",\"items\":[\"x\"]},{\"version\":\"1.0.2\",\"items\":[]}]");
        Assert.Single(parsed);
    }

    [Fact]
    public void The_notes_shipped_with_the_app_cover_the_version_that_is_running()
    {
        var entries = WhatsNew.Load();
        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => Version.Parse(e.Version) == Updater.CurrentVersion); // fails if a release goes out without its note
        Assert.All(entries, e => Assert.All(e.Items, i => Assert.False(string.IsNullOrWhiteSpace(i))));
        var versions = entries.Select(e => Version.Parse(e.Version)).ToList();
        Assert.Equal(versions.OrderByDescending(v => v), versions); // newest first
    }

    [Fact]
    public void A_fresh_install_has_no_settings_file_so_it_is_not_told_what_is_new()
    {
        Assert.False(new AppSettings().IsFirstRun); // only Load() sets it, from whether a settings file existed
    }
}
