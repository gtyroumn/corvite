using System.IO;
using System.Net.Http;
using Corvite.Core.Ai;

namespace Corvite.App.Services;

/// <summary>
/// The advisor's game list can grow without a new app release: at start the app downloads games.json from the project repository
/// (public file, no data sent), checks it and keeps a copy next to the settings. The player's own games live there too.
/// A broken or unreachable file changes nothing.
/// </summary>
public static class GameListUpdater
{
    private const string Url = "https://raw.githubusercontent.com/gtyroumn/corvite/main/src/Corvite.Core/Data/games.json";

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Corvite");

    /// <summary>Tells the game list where the downloaded copy and the player's games are stored. Call before the advisor is used.</summary>
    public static void Configure()
    {
        GameDb.CachePath = Path.Combine(Dir, "games.cache.json");
        GameDb.UserPath = Path.Combine(Dir, "games.user.json");
        GameDb.Reload();
    }

    public static async Task<int> UpdateAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Corvite-game-list");
            var json = await http.GetStringAsync(Url);
            return GameDb.ApplyUpdate(json);
        }
        catch { return 0; }   // offline or blocked: keep what we have
    }
}
