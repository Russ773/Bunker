using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>A newer version of a community widget you have installed, found in the widget library.</summary>
public sealed record WidgetUpdate(WidgetDef Installed, WidgetDef Offered, string Json);

/// <summary>
/// Looks in the community widget library for newer versions of the widgets you have installed, so you do not have to open each
/// widget to find out. Nothing is changed by the check: installing an update still shows you what the widget can do first.
/// </summary>
public sealed class WidgetUpdateService
{
    private static readonly Regex SafeId = new(@"^[a-z0-9][a-z0-9_-]{1,40}$", RegexOptions.Compiled);
    private readonly Core _core;
    private List<WidgetUpdate> _available = new();

    public IReadOnlyList<WidgetUpdate> Available => _available;
    public bool Checking { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>The list of updates changed, or a check started or finished.</summary>
    public event Action? Changed;

    public WidgetUpdateService(Core core)
    {
        _core = core;
        // Once an update has been installed (or the widget removed) it drops off the list.
        _core.Widgets.Changed += () =>
        {
            _available = _available.Where(u => _core.Widgets.Get(u.Installed.Id) is { } now && IsNewer(u.Offered.Version, now.Version)).ToList();
            Changed?.Invoke();
        };
    }

    public bool HasUpdate(string? widgetId) => widgetId is not null && _available.Any(u => u.Installed.Id == widgetId);
    public WidgetUpdate? Find(string? widgetId) => _available.FirstOrDefault(u => u.Installed.Id == widgetId);

    /// <summary>True if <paramref name="offered"/> is a higher version than <paramref name="installed"/> (both like "1.2.3").</summary>
    public static bool IsNewer(string? offered, string? installed) =>
        Version.TryParse(offered, out var a) && Version.TryParse(installed, out var b) && a > b;

    private string StampFile => Path.Combine(AppSettings.Folder, "widgets-checked.txt");

    /// <summary>Checks the library. Without <paramref name="force"/> it only does so if the last check was more than 12 hours ago.</summary>
    public async Task CheckAsync(bool force = false)
    {
        if (Checking) return;
        var installed = _core.Widgets.All.ToList();
        if (installed.Count == 0) { LastChecked = DateTime.Now; Changed?.Invoke(); return; }
        if (!force && File.Exists(StampFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(StampFile) < TimeSpan.FromHours(12)) return;

        Checking = true;
        LastError = null;
        Changed?.Invoke();
        try
        {
            // The library is a plain index file on raw.githubusercontent.com (not limited like GitHub's API). Each widget file is named after its id.
            var baseUrl = $"https://raw.githubusercontent.com/{Updater.Repo}/main/widgets/";
            using var doc = JsonDocument.Parse(await WidgetNet.GetAsync(baseUrl + "index.json"));
            var files = doc.RootElement.GetProperty("widgets").EnumerateArray().Select(e => e.GetString() ?? "")
                .Where(n => n.EndsWith(".json", StringComparison.Ordinal)).ToHashSet();

            var found = new List<WidgetUpdate>();
            foreach (var have in installed)
            {
                if (!SafeId.IsMatch(have.Id) || !files.Contains(have.Id + ".json")) continue;
                try
                {
                    var json = await WidgetNet.GetAsync(baseUrl + have.Id + ".json");
                    if (WidgetRules.Parse(json, out _) is { } offered && offered.Id == have.Id && IsNewer(offered.Version, have.Version))
                        found.Add(new WidgetUpdate(have, offered, json));
                }
                catch { /* skip one that cannot be fetched */ }
            }
            _available = found;
            try { Directory.CreateDirectory(AppSettings.Folder); File.WriteAllText(StampFile, DateTime.UtcNow.ToString("O")); } catch { }
            LastChecked = DateTime.Now;
        }
        catch (Exception e) { LastError = e.Message; }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }

        if (_available.Count > 0)
            _core.Log.Add($"Widget updates available: {string.Join(", ", _available.Select(u => u.Installed.Name))}. See Settings → Updates.", LogKind.Info);
    }

    /// <summary>Shows what the new version can do and, if you agree, installs it over the old one.</summary>
    public bool ReviewAndInstall(WidgetUpdate update, IWin32Window? owner)
    {
        using var form = new WidgetReviewForm(update.Offered, install: true);
        if (form.ShowDialog(owner) != DialogResult.OK) return false;
        _core.Widgets.Install(update.Offered, update.Json);
        return true;
    }
}
