namespace Hastane.Identity.Api.Settings;

// Notification servisine servisten servise çağrı ayarları (Services:NotificationBaseUrl + Internal:ApiKey).
public class NotificationClientOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5112";
    // Sır: user-secrets/ortam değişkeni (Internal:ApiKey). appsettings'te boş yer tutucu.
    public string ApiKey { get; set; } = string.Empty;
}

// Şifre sıfırlama bağlantısının kökü. İstekten (Host başlığı) ASLA türetilmez: Host başlığı zehirlemesi olmasın.
public class WebUiOptions
{
    public const string SectionName = "WebUi";
    public string BaseUrl { get; set; } = "http://localhost:5010";
}

public class StorageOptions
{
    public const string SectionName = "Storage";
    // Depo dışı: varsayılan %LOCALAPPDATA%/hastane-identity/photos
    public string PhotoPath { get; set; } = string.Empty;
}
