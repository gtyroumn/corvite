using System.Text.Json;
using System.Text.RegularExpressions;

namespace Corvite.Core.Ai;

/// <summary>A game known to the advisor: how to recognise it in a question and the in-game settings text per language (ru / uk / en).</summary>
public sealed record Game(string Id, Regex Pattern, bool Heavy, IReadOnlyDictionary<string, string> InGame);

/// <summary>Game list loaded from Data/games.json (embedded resource).</summary>
public static class GameDb
{
    private sealed class Dto
    {
        public string Id { get; set; } = "";
        public string Regex { get; set; } = "";
        public bool Heavy { get; set; }
        public Dictionary<string, string> Ig { get; set; } = new();
    }

    private static IReadOnlyList<Game>? _all;

    public static IReadOnlyList<Game> All
    {
        get
        {
            if (_all != null) return _all;
            using var s = typeof(GameDb).Assembly.GetManifestResourceStream("Corvite.Core.Data.games.json");
            var list = s == null ? new List<Dto>()
                : JsonSerializer.Deserialize<List<Dto>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Dto>();
            _all = list.Select(d => new Game(d.Id,
                new Regex(d.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)),
                d.Heavy, d.Ig)).ToList();
            return _all;
        }
    }

    /// <summary>First game whose pattern matches the (already lower-cased) question, or null.</summary>
    public static Game? Find(string text)
    {
        foreach (var g in All)
        {
            try { if (g.Pattern.IsMatch(text)) return g; } catch (RegexMatchTimeoutException) { }
        }
        return null;
    }
}
