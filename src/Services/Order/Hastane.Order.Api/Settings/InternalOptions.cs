namespace Hastane.Order.Api.Settings;

// Servisler arası iç anahtar ("Internal:ApiKey", user-secrets/ortam; Market ile AYNI değer). Order'ın kendi iç ucu yok:
// anahtar yalnız Market'in /api/internal/stock/* uçlarını çağırırken X-Internal-Key olarak gönderilir.
public class InternalOptions
{
    public const string SectionName = "Internal";

    public string ApiKey { get; set; } = string.Empty;
}
