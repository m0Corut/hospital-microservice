using System.ComponentModel.DataAnnotations;

namespace Hastane.Market.Api.Settings;

// appsettings "Market" bölümü. Süreler tek yerde: koda gömülü sihirli sayı kalmasın.
public class MarketOptions
{
    public const string SectionName = "Market";

    // Ürün önbelleği (cache-aside) ömrü: yazma/stok değişiminde anahtarlar zaten silinir, süre yalnız emniyet payıdır.
    [Range(1, 1440)]
    public int ProductCacheMinutes { get; set; } = 5;

    // Sepet ömrü; her yazmada yeniden başlar (terk edilmiş sepetler kendiliğinden kaybolur).
    [Range(1, 90)]
    public int CartDays { get; set; } = 7;

    // Yalnız geliştirmede (launchSettings) demo ürünleri yükler; testlerde ve üretimde kapalı.
    public bool SeedDemoData { get; set; }
}
