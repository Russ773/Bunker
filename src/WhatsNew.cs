using System.Drawing;
using System.Reflection;
using System.Text.Json;

namespace Bunker;

/// <summary>The changes in one version, written for the person using the app.</summary>
public sealed record WhatsNewEntry(string Version, string[] Items);

/// <summary>The "what's new" notes shown after an update. They live in assets/whatsnew.json and ship inside the app.</summary>
public static class WhatsNew
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<WhatsNewEntry> Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("whatsnew.json");
            if (stream is null) return Array.Empty<WhatsNewEntry>();
            return Parse(new StreamReader(stream).ReadToEnd());
        }
        catch { return Array.Empty<WhatsNewEntry>(); }
    }

    public static IReadOnlyList<WhatsNewEntry> Parse(string json)
    {
        var entries = JsonSerializer.Deserialize<List<WhatsNewEntry>>(json, Options) ?? new();
        return entries.Where(e => Version.TryParse(e.Version, out _) && e.Items is { Length: > 0 }).ToList();
    }

    /// <summary>
    /// The notes for versions newer than <paramref name="lastSeen"/> up to the one running now, newest first, at most <paramref name="max"/>.
    /// Notes for versions that are not out yet are never shown.
    /// </summary>
    public static List<WhatsNewEntry> Since(IEnumerable<WhatsNewEntry> entries, Version lastSeen, Version current, int max = 5) =>
        entries.Select(e => (Entry: e, Version: Version.Parse(e.Version)))
            .Where(x => x.Version > lastSeen && x.Version <= current)
            .OrderByDescending(x => x.Version)
            .Take(max)
            .Select(x => x.Entry)
            .ToList();

    /// <summary>The newest few notes up to the running version, for the "What's new" button in Settings.</summary>
    public static List<WhatsNewEntry> Latest(IEnumerable<WhatsNewEntry> entries, Version current, int max = 5) =>
        Since(entries, new Version(0, 0, 0), current, max);
}

/// <summary>A small themed window listing what changed. Shown once after an update, and from Settings → About.</summary>
public sealed class WhatsNewForm : Form
{
    public WhatsNewForm(IReadOnlyList<WhatsNewEntry> entries, string heading)
    {
        Theme.Apply(this);
        Text = "What's new in Bunker";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 560);

        var ok = Theme.PrimaryButton("Got it");
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Bg, Padding = new Padding(28, 14, 28, 14) };
        ok.Dock = DockStyle.Right;
        bar.Controls.Add(ok);

        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(28, 6, 12, 8), BackColor = Theme.Bg };
        DarkScroll.Apply(list);
        list.Controls.Add(new Label { Text = heading, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 14, 0, 12) });
        foreach (var entry in entries)
        {
            list.Controls.Add(new Label { Text = "Version " + entry.Version, Font = Theme.Semibold, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 10, 0, 4) });
            foreach (var item in entry.Items)
                list.Controls.Add(new Label { Text = "•  " + item, ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(8, 2, 0, 3) });
        }

        Controls.Add(list);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = CancelButton = ok;
        ok.Click += (_, _) => Close();
    }
}
