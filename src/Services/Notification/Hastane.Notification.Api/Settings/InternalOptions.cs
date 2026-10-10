namespace Hastane.Notification.Api.Settings;

// Servisler arası anahtar ("Internal:ApiKey", user-secrets/ortam). Boşsa iç uçlar HER ZAMAN 401 verir.
public class InternalOptions
{
    public const string SectionName = "Internal";

    public string ApiKey { get; set; } = string.Empty;
}
