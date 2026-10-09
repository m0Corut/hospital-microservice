using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Settings;

// appsettings.json "Billing" bölümü. Vergi oranı ve kabul edilen para birimleri koda gömülü değil, yapılandırmadan gelir.
public class BillingOptions
{
    public const string SectionName = "Billing";

    [Range(0, 100)]
    public decimal TaxPercent { get; set; } = 10m;

    // Pending satırı bu yaştan eskiyse (süreç çökmüş olabilir) aynı anahtarla yeniden denemede beklenmez, hemen 409 döner.
    [Range(1, 86400)]
    public int PendingStaleSeconds { get; set; } = 120;

    // Taze bir Pending'in sonucunu en çok bu kadar bekle.
    [Range(0, 60)]
    public int PendingWaitSeconds { get; set; } = 15;

    [MinLength(1)]
    public string[] AllowedCurrencies { get; set; } = [];
}
