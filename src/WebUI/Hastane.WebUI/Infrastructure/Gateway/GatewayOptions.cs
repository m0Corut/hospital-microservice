namespace Hastane.WebUI.Infrastructure.Gateway;

// "Gateway" yapılandırma bölümü. Tüm API çağrıları tek kapıdan (Ocelot) geçer; servis adresleri WebUI'da yok.
public class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string BaseUrl { get; set; } = "http://localhost:5000";
    public int TimeoutSeconds { get; set; } = 15;
}
