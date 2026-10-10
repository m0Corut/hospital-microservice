using System.ComponentModel.DataAnnotations;

namespace Hastane.Order.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü).
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string MarketBaseUrl { get; set; } = "http://localhost:5109";

    // Market çağrıları için zaman aşımı: yanıt gelmezse ödeme/stok durumu belirsiz sayılır, sipariş 503 ile reddedilir.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}
