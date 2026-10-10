using System.Text;

namespace Hastane.Pharmacy.Api.Common;

// "Parasetamol", "PARASETAMOL" ve "parasetamol" ile "Ağrı Kesici" / "AGRI KESICI" aynı ilaç mı? Karşılamada reçetedeki serbest metin
// katalogdaki adla bununla eşleşir. Neden ToLower yetmez: Türkçe'de "I" küçülünce "ı", "İ" küçülünce "i" olur;
// kültüre bağlı ToLower sunucu ayarına göre farklı sonuç verir. Biz harfleri açıkça ASCII karşılığına indirgeriz.
public static class TurkishText
{
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasSpace = true; // baştaki boşlukları at
        foreach (var raw in value.Trim().Normalize(NormalizationForm.FormC))
        {
            var c = raw switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' or 'I' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => char.ToLowerInvariant(raw),
            };

            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace) continue; // ardışık boşlukları teke indir
                sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
