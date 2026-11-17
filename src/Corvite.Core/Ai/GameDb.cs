using System.Text.Json;
using System.Text.RegularExpressions;

namespace Corvite.Core.Ai;

/// <summary>A game known to the advisor: how to recognise it in a question and the in-game settings text per language (ru / uk / en).</summary>
public sealed record Game(string Id, Regex Pattern, bool Heavy, IReadOnlyDictionary<string, string> InGame, bool IsUser = false);

/// <summary>
/// Game list. Sources, in order: the embedded Data/games.json, an updated copy downloaded by the app (CachePath; it can add games and
/// replace a game only when its Rev is higher), and the player's own games (UserPath). User games are matched first.
/// </summary>
public static class GameDb
{
    private sealed class Dto
    {
        public string Id { get; set; } = "";
        public string Regex { get; set; } = "";
        public bool Heavy { get; set; }
        public int Rev { get; set; }
        public Dictionary<string, string> Ig { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly object Gate = new();
    private static IReadOnlyList<Game>? _all;

    /// <summary>Where the downloaded update is stored (set by the app at start).</summary>
    public static string? CachePath { get; set; }

    /// <summary>Where the player's own games are stored (set by the app at start).</summary>
    public static string? UserPath { get; set; }

    public static IReadOnlyList<Game> All
    {
        get { lock (Gate) return _all ??= Load(); }
    }

    public static void Reload() { lock (Gate) _all = null; }

    private static Game? Make(Dto d, bool user)
    {
        if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Regex) || d.Ig.Count == 0) return null;
        try
        {
            return new Game(d.Id, new Regex(d.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)), d.Heavy, d.Ig, user);
        }
        catch (ArgumentException) { return null; }
    }

    private static List<Dto> Parse(Stream s) =>
        JsonSerializer.Deserialize<List<Dto>>(s, Opt) ?? new List<Dto>();

    private static List<Dto> Embedded()
    {
        using var s = typeof(GameDb).Assembly.GetManifestResourceStream("Corvite.Core.Data.games.json");
        return s == null ? new List<Dto>() : Parse(s);
    }

    private static List<Dto> FromFile(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path)) return new List<Dto>();
            using var f = File.OpenRead(path);
            return Parse(f);
        }
        catch { return new List<Dto>(); }   // a broken file must never break the advisor
    }

    private static IReadOnlyList<Game> Load()
    {
        var list = Embedded();
        foreach (var c in FromFile(CachePath))
        {
            var i = list.FindIndex(e => string.Equals(e.Id, c.Id, StringComparison.OrdinalIgnoreCase));
            if (i < 0) list.Insert(Math.Max(0, list.FindIndex(e => IsGenre(e.Id))), c);   // new games go before the generic genres
            else if (c.Rev > list[i].Rev) list[i] = c;
        }
        var res = list.Select(d => Make(d, false)).Where(g => g != null).Select(g => g!).ToList();
        var mine = FromFile(UserPath).Select(d => Make(d, true)).Where(g => g != null).Select(g => g!).ToList();
        mine.AddRange(res);
        return mine;
    }

    private static bool IsGenre(string id) => id is "FPS" or "MOBA" or "TPS";

    /// <summary>First game whose pattern matches the (already lower-cased) question, or null.</summary>
    public static Game? Find(string text)
    {
        foreach (var g in All)
        {
            try { if (g.Pattern.IsMatch(text)) return g; } catch (RegexMatchTimeoutException) { }
        }
        return null;
    }

    /// <summary>Like Find, but forgives one or two typos in a word of the game name ("valorat", "аппекс"). Null when unsure.</summary>
    public static Game? FindFuzzy(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(w => w.Length >= 5).ToList();
        if (words.Count == 0) return null;
        Game? best = null; var bd = int.MaxValue;
        foreach (var g in All.Where(g => !IsGenre(g.Id)))
        {
            foreach (var t in Regex.Matches(g.Id.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(t => t.Length >= 5))
                foreach (var w in words)
                {
                    var max = Fuzzy.Allowed(Math.Min(w.Length, t.Length));
                    if (max == 0) continue;
                    var d = Fuzzy.Distance(w, t, max);
                    if (d <= max && d < bd) { bd = d; best = g; }
                }
        }
        return best;
    }

    // ------------------------------------------------------------ the player's own games
    /// <summary>Adds (or replaces) a game of the player: name, genre FPS / MOBA / TPS (its in-game settings text is reused). Returns null when the input is not usable.</summary>
    public static Game? AddUser(string name, string genre)
    {
        name = Regex.Replace(name.Trim(), @"\s+", " ");
        if (UserPath == null || name.Length < 2 || name.Length > 40) return null;
        var tpl = Embedded().FirstOrDefault(e => string.Equals(e.Id, genre, StringComparison.OrdinalIgnoreCase) && IsGenre(e.Id));
        if (tpl == null) return null;
        var pattern = Regex.Escape(name.ToLowerInvariant()).Replace("\\ ", "[ -]?");
        var dto = new Dto { Id = name, Regex = pattern, Heavy = tpl.Heavy, Ig = new Dictionary<string, string>(tpl.Ig) };
        if (Make(dto, true) == null) return null;
        var list = FromFile(UserPath);
        list.RemoveAll(e => string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, dto);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
            File.WriteAllText(UserPath, JsonSerializer.Serialize(list, Opt));
        }
        catch { return null; }
        Reload();
        return All.FirstOrDefault(g => g.IsUser && string.Equals(g.Id, name, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------ update without a new app release
    /// <summary>Checks a downloaded games.json and, when it is valid, stores it as the update. Returns the number of games, 0 when rejected.</summary>
    public static int ApplyUpdate(string json)
    {
        if (CachePath == null) return 0;
        try
        {
            var list = JsonSerializer.Deserialize<List<Dto>>(json, Opt);
            if (list == null || list.Count < 5) return 0;
            if (list.Any(d => Make(d, false) == null)) return 0;   // one broken entry rejects the whole file
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, json);
            Reload();
            return list.Count;
        }
        catch { return 0; }
    }
}
