namespace Corvite.Core.Ai;

/// <summary>Typo tolerance for the advisor: edit distance and "closest known word".</summary>
public static class Fuzzy
{
    /// <summary>Levenshtein distance; returns max + 1 as soon as it is clear the distance exceeds max.</summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            var rowMin = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (cur[j] < rowMin) rowMin = cur[j];
            }
            if (rowMin > max) return max + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>The single closest word of the vocabulary within maxDist (and not identical); null when none or when two are equally close.</summary>
    public static string? Closest(string word, IEnumerable<string> vocab, int maxDist)
    {
        string? best = null; var bd = maxDist + 1; var tie = false;
        foreach (var v in vocab)
        {
            var d = Distance(word, v, maxDist);
            if (d > maxDist) continue;
            if (d < bd) { bd = d; best = v; tie = false; }
            else if (d == bd && v != best) tie = true;
        }
        return tie ? null : best;
    }

    /// <summary>Allowed number of typos for a word of this length: short words must be exact.</summary>
    public static int Allowed(int length) => length >= 7 ? 2 : length >= 5 ? 1 : 0;
}
