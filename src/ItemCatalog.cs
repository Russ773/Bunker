using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Bunker;

/// <summary>One Rust item as shown in the item search.</summary>
public sealed record ItemInfo(string Shortname, string Name, string Category, string Description = "");

/// <summary>
/// The list of Rust items (name, shortname, category) and their pictures. It comes from the open
/// bobosneefdev/rust-items-json project, which re-reads the game's own files whenever Rust updates, so new items appear on their own.
/// Bunker keeps a copy on this PC, checks for a newer build at most once a day, and falls back to a list built into the app when offline.
/// </summary>
public sealed class ItemCatalog
{
    public const string Source = "https://github.com/bobosneefdev/rust-items-json";
    private const string DataUrl = "https://raw.githubusercontent.com/bobosneefdev/rust-items-json/master/data/";
    private const int IconSize = 64;

    private static readonly Regex SafeShortname = new(@"^[A-Za-z0-9._-]{1,80}$", RegexOptions.Compiled);
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-Items");
        return c;
    }

    private readonly string _folder = Path.Combine(AppSettings.Folder, "items");
    private readonly Func<bool> _online;
    private readonly ConcurrentDictionary<string, Bitmap> _icons = new();
    private readonly ConcurrentDictionary<string, DateTime> _failed = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly SemaphoreSlim _downloads = new(4);
    private List<ItemInfo> _items = new();

    /// <summary>The Rust build the list was made from, when known.</summary>
    public string? Build { get; private set; }

    public IReadOnlyList<ItemInfo> Items => _items;

    /// <summary>The list changed (a newer one was downloaded).</summary>
    public event Action? Changed;

    /// <summary>A picture finished loading. Raised on a background thread; handlers must switch to the UI thread.</summary>
    public event Action<string>? IconLoaded;

    public ItemCatalog(Func<bool> online)
    {
        _online = online;
        _items = LoadCached() ?? LoadBuiltIn();
    }

    // ---------------------------------------------------------------- reading the list

    /// <summary>Reads the item list from the project's items.json. Hidden items (internal ones nobody can give) are left out.</summary>
    public static List<ItemInfo>? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<ItemInfo>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (e.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                var shortname = Str(e, "shortname");
                if (!SafeShortname.IsMatch(shortname)) continue;
                var name = Str(e, "name");
                list.Add(new ItemInfo(shortname, name.Length > 0 ? name : shortname, Str(e, "category"), Str(e, "description")));
            }
            return list.Count > 0 ? list : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private List<ItemInfo>? LoadCached()
    {
        try
        {
            var file = Path.Combine(_folder, "items.json");
            if (!File.Exists(file)) return null;
            var meta = Path.Combine(_folder, "build.txt");
            if (File.Exists(meta)) Build = File.ReadAllText(meta).Trim();
            return Parse(File.ReadAllText(file));
        }
        catch { return null; }
    }

    private static List<ItemInfo> LoadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("items-fallback.json");
        if (stream is null) return new();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? new();
    }

    // ---------------------------------------------------------------- keeping it current

    /// <summary>Checks the project's small build file; if Rust has been updated since the saved copy, downloads the new list.</summary>
    public async Task RefreshAsync()
    {
        if (!_online()) return;
        try
        {
            var stamp = Path.Combine(_folder, "checked.txt");
            if (File.Exists(stamp) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromHours(20)) return;

            string? latest = null;
            using (var doc = JsonDocument.Parse(await Http.GetStringAsync(DataUrl + "meta.json")))
                latest = Str(doc.RootElement, "build");

            Directory.CreateDirectory(_folder);
            if (latest is { Length: > 0 } && (latest != Build || !File.Exists(Path.Combine(_folder, "items.json"))))
            {
                var json = await Http.GetStringAsync(DataUrl + "items.json");
                if (Parse(json) is { } parsed)
                {
                    await File.WriteAllTextAsync(Path.Combine(_folder, "items.json"), json);
                    await File.WriteAllTextAsync(Path.Combine(_folder, "build.txt"), latest);
                    Build = latest;
                    _items = parsed;
                    Changed?.Invoke();
                }
            }
            await File.WriteAllTextAsync(stamp, DateTime.UtcNow.ToString("O"));
        }
        catch { /* offline or GitHub unreachable: keep using what we have */ }
    }

    // ---------------------------------------------------------------- searching

    public ItemInfo? Find(string shortname) =>
        _items.FirstOrDefault(i => i.Shortname.Equals(shortname.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Items matching what was typed, best matches first: exact, then names that start with it, then names or shortnames that contain it.</summary>
    public IReadOnlyList<ItemInfo> Search(string query, int take = 8)
    {
        var q = query.Trim();
        if (q.Length == 0) return Array.Empty<ItemInfo>();

        int Score(ItemInfo i)
        {
            if (i.Shortname.Equals(q, StringComparison.OrdinalIgnoreCase) || i.Name.Equals(q, StringComparison.OrdinalIgnoreCase)) return 0;
            if (i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1;
            if (i.Shortname.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 2;
            if (i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 3;
            if (i.Shortname.Contains(q, StringComparison.OrdinalIgnoreCase)) return 4;
            if (i.Category.Contains(q, StringComparison.OrdinalIgnoreCase)) return 5;
            return -1;
        }

        return _items.Select(i => (Item: i, Score: Score(i))).Where(x => x.Score >= 0)
            .OrderBy(x => x.Score).ThenBy(x => x.Item.Name.Length).ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take).Select(x => x.Item).ToList();
    }

    // ---------------------------------------------------------------- pictures

    /// <summary>The item's picture if it is ready. Otherwise null, and it starts loading in the background (then <see cref="IconLoaded"/> fires).</summary>
    public Bitmap? IconIfReady(string shortname)
    {
        if (!SafeShortname.IsMatch(shortname)) return null;
        if (_icons.TryGetValue(shortname, out var ready)) return ready;
        if (_failed.TryGetValue(shortname, out var when) && DateTime.UtcNow - when < TimeSpan.FromMinutes(10)) return null;
        if (_pending.TryAdd(shortname, 0)) _ = LoadIconAsync(shortname);
        return null;
    }

    private async Task LoadIconAsync(string shortname)
    {
        await _downloads.WaitAsync();
        try
        {
            var file = Path.Combine(_folder, "icons", shortname + ".png");
            byte[]? png = null;
            if (File.Exists(file)) png = await File.ReadAllBytesAsync(file);
            else if (_online())
            {
                var webp = await Http.GetByteArrayAsync($"{DataUrl}icons/{shortname}.webp");
                png = ToPng(webp);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, png);
            }

            if (png is null) { _failed[shortname] = DateTime.UtcNow; return; }
            using var ms = new MemoryStream(png);
            using var loaded = new Bitmap(ms);
            _icons[shortname] = new Bitmap(loaded); // a copy, so the stream can be released
            IconLoaded?.Invoke(shortname);
        }
        catch { _failed[shortname] = DateTime.UtcNow; }
        finally { _pending.TryRemove(shortname, out _); _downloads.Release(); }
    }

    /// <summary>Converts the project's WebP picture into a small PNG that Windows Forms can draw.</summary>
    public static byte[] ToPng(byte[] webp)
    {
        using var source = SKBitmap.Decode(webp) ?? throw new InvalidDataException("Not a picture Bunker can read.");
        using var small = source.Resize(new SKImageInfo(IconSize, IconSize), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                          ?? throw new InvalidDataException("The picture could not be resized.");
        using var image = SKImage.FromBitmap(small);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
