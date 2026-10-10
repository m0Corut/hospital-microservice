using System.ComponentModel.DataAnnotations;

namespace Hastane.Pharmacy.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü). Adres varsayılan geliştirme portudur.
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string PrescriptionBaseUrl { get; set; } = "http://localhost:5105";

    // Reçete okunamazsa karşılama 503 ile durur (stoğa dokunulmaz); zaman aşımı bu kadar saniye.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}
