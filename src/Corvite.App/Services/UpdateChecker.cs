using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Corvite.App.Services;

public sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Looks for a newer release on GitHub (public API, one request at start, no data sent). The user downloads and
/// installs it manually: Corvite needs administrator rights, so it does not replace itself.
/// </summary>
public static class UpdateChecker
{
    private const string Repo = "gtyroumn/corvite";
    private const string Prefix = "https://github.com/" + Repo + "/";

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Corvite-update-check");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var text = tag.TrimStart('v', 'V');
            var dash = text.IndexOf('-');
            if (dash > 0) text = text[..dash];
            if (!Version.TryParse(text, out var latest)) return null;
            var cur = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Norm(latest) > Norm(cur) ? new UpdateInfo(text, url) : null;
        }
        catch { return null; }   // offline, rate limit, no releases yet: stay silent
    }

    private static Version Norm(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
