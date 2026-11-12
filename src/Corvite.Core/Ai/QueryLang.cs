using System.Text.RegularExpressions;

namespace Corvite.Core.Ai;

/// <summary>The advisor answers in the language of the question, not the interface language (TZ section 10).</summary>
public static class QueryLang
{
    private static readonly Regex UkWords = new(
        "привіт|будь ласка|ризик|який|яка|які|налаштув|підбер|пам'ят|ігр|потрібн|допомож|найкращ",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Returns "ru", "uk" or "en"; when the text has no letters the fallback (interface language) is used.</summary>
    public static string Detect(string text, string fallback)
    {
        var x = text.ToLowerInvariant();
        if (x.IndexOfAny(new[] { 'ы', 'э', 'ъ', 'ё' }) >= 0) return "ru";
        if (x.IndexOfAny(new[] { 'і', 'ї', 'є', 'ґ' }) >= 0 || UkWords.IsMatch(x)) return "uk";
        if (x.Any(c => c is >= 'а' and <= 'я')) return "ru";
        if (x.Any(c => c is >= 'a' and <= 'z')) return "en";
        return fallback;
    }
}
