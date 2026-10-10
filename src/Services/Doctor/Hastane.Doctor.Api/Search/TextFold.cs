using System.Globalization;
using System.Text;

namespace Hastane.Doctor.Api.Search;

// Dizindeki "turkish lowercase + asciifolding" ile aynı fikrin C# karşılığı: öneri adaylarını süzerken kullanılır
// ("Yilmaz" yazan kullanıcıya "Yılmaz" önerilsin). Yalnız öneri eşleştirmesinde; arama kararı dizinin analizöründedir.
public static class TextFold
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Türkçe küçük harf elle (kültür verisi gerektirmez): İ -> i, I -> ı.
        var lower = text.Trim().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
        var d = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch switch { 'ı' => 'i', 'ß' => 's', _ => ch });
        }
        return sb.ToString();
    }

    public static string[] Tokens(string? text)
        => Fold(text).Split([' ', '-', '.', ',', '\'', '’'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Sorgunun HER sözcüğü adayın bir sözcüğünün öneki ise eşleşir ("yil" -> "Ali Yılmaz", "ali y" -> "Ali Yılmaz").
    public static bool MatchesPrefixes(string candidate, string query)
    {
        var q = Tokens(query);
        if (q.Length == 0) return false;
        var c = Tokens(candidate);
        return q.All(qt => c.Any(ct => ct.StartsWith(qt, StringComparison.Ordinal)));
    }
}
