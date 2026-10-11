using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Bunker;

public record UpdateInfo(Version Version, string DownloadUrl, string Notes);

/// <summary>Checks GitHub Releases for a newer installer and runs it silently.</summary>
public static class Updater
{
    public const string Repo = "Russ773/Bunker";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Returns the newer release, or null if already up to date. Throws on network/parse errors.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latest) || latest <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, notes);
            }
        }
        throw new InvalidOperationException($"Release v{latest} has no installer attached.");
    }

    /// <summary>Downloads the installer and launches it silently. The caller should exit the app afterwards.</summary>
    public static async Task DownloadAndLaunchAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Bunker-Setup-{update.Version}.exe");
        using (var resp = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total));
            }
        }

        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    }
}
