using System.Globalization;

namespace Hastane.Queue.Api.Services;

// Bekleme salonu ekranı herkese açık: tam ad yerine "Can Y." (ad + soyadın baş harfi). Tam ad hiçbir yerde saklanmaz.
public static class DisplayNameMasker
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    public const string Fallback = "Hasta";
    private const int MaxFirstNameLength = 30;

    public static string Mask(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Fallback;

        var first = parts[0].Length > MaxFirstNameLength ? parts[0][..MaxFirstNameLength] : parts[0];
        if (parts.Length == 1) return first;

        // Son parça soyad kabul edilir ("Ahmet Can Yılmaz" -> "Ahmet Y."); baş harf Türkçe kurala göre büyütülür (i -> İ).
        var initial = char.ToUpper(parts[^1][0], Tr);
        return $"{first} {initial}.";
    }
}
