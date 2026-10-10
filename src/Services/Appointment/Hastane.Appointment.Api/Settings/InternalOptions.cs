namespace Hastane.Appointment.Api.Settings;

// Servisler arası iç anahtar ("Internal:ApiKey", user-secrets/ortam; Notification ile AYNI değer). Boşsa bildirim gönderilmez.
public class InternalOptions
{
    public const string SectionName = "Internal";

    public string ApiKey { get; set; } = string.Empty;
}
