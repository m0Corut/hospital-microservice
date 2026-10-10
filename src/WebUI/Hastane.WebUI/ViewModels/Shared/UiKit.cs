using System.Globalization;
using System.Text;

namespace Hastane.WebUI.ViewModels.Shared;

// Görünümlerin ortak küçük yardımcıları: avatar (fotoğraf yoksa baş harfli SVG), branş ikonu, yıldız.
public static class UiKit
{
    private static readonly string[] AvatarColors =
        ["#1e6fd9", "#0fb5a6", "#7a55d1", "#e8833a", "#d9467a", "#4aa84a", "#2f86e8", "#c2365f"];

    // "Ayşe Kaya" -> "AK", "Dr. Ali" -> "A": unvanlar ("Dr.", "Prof.") atlanır.
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.EndsWith('.'))
            .ToList();
        if (words.Count == 0) return "?";
        var first = char.ToUpper(words[0][0], CultureInfo.GetCultureInfo("tr-TR"));
        if (words.Count == 1) return first.ToString();
        var last = char.ToUpper(words[^1][0], CultureInfo.GetCultureInfo("tr-TR"));
        return string.Concat(first, last);
    }

    // Aynı ad hep aynı rengi alsın (rastgele değil, ada bağlı).
    public static string AvatarColor(string? seed)
    {
        var hash = 17;
        foreach (var c in seed ?? string.Empty) hash = unchecked(hash * 31 + c);
        return AvatarColors[(hash & 0x7fffffff) % AvatarColors.Length];
    }

    // Yalnız yerel yol ("/img/...") ya da http/https adresi görsel olarak kabul edilir; başka şema (javascript:, data:) atılır.
    public static string? SafeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\')) return url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;
    }

    // Branş adından ikon dosyası (wwwroot/img/branches/<anahtar>.svg). Eşleşmezse genel ikon.
    public static string BranchIcon(string? branchName)
    {
        var key = Fold(branchName);
        string icon = key switch
        {
            _ when HasAny(key, "kardiyo", "kalp") => "cardiology",
            _ when HasAny(key, "norolo", "beyin", "sinir") => "neurology",
            _ when HasAny(key, "dis", "agiz", "dental") => "dental",
            _ when HasAny(key, "goz", "oftalmo") => "ophthalmology",
            _ when HasAny(key, "ortoped", "kemik", "travmato") => "orthopedics",
            _ when HasAny(key, "cocuk", "pediat") => "pediatrics",
            _ when HasAny(key, "cildiye", "dermato", "cilt") => "dermatology",
            _ when HasAny(key, "uroloji", "bobrek", "nefro") => "urology",
            _ => "general",
        };
        return $"/img/branches/{icon}.svg";
    }

    public static int StarCount(double rating) => (int)Math.Clamp(Math.Round(rating, MidpointRounding.AwayFromZero), 0, 5);

    public static string Stars(double rating)
    {
        var full = StarCount(rating);
        return new string('★', full);
    }

    public static string EmptyStars(double rating) => new('☆', 5 - StarCount(rating));

    // Türkçe harfleri ve büyük/küçük farkını atıp karşılaştırma anahtarı üretir ("Göz Hastalıkları" -> "goz hastaliklari").
    private static string Fold(string? text)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? string.Empty).ToLower(CultureInfo.GetCultureInfo("tr-TR")))
        {
            sb.Append(ch switch { 'ı' => 'i', 'ğ' => 'g', 'ü' => 'u', 'ş' => 's', 'ö' => 'o', 'ç' => 'c', _ => ch });
        }

        return sb.ToString();
    }

    private static bool HasAny(string haystack, params string[] needles) => needles.Any(haystack.Contains);
}

// Avatar bileşeni girdisi: fotoğraf adresi (olabilir null), görünen ad, boyut (sm/md/lg/xl).
public sealed record AvatarViewModel(string? ImageUrl, string Name, string Size = "md");

public sealed record PanelMenuItem(string Icon, string Label, string Area, string Controller, string Action = "Index");

// Panel kenar çubuğu: profil kartı (avatar, ad, rol) + menü.
public sealed record PanelSideViewModel(string DisplayName, string RoleLabel, string? ImageUrl, IReadOnlyList<PanelMenuItem> Items);
