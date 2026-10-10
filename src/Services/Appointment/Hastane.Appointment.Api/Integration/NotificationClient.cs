using Hastane.Appointment.Api.Settings;
using Microsoft.Extensions.Options;

namespace Hastane.Appointment.Api.Integration;

// Notification servisine (iç uç) typed HttpClient ile bildirim yollar. Randevu ZATEN kaydedilmiştir: buradaki hiçbir hata
// isteğin sonucunu değiştirmez. İstemci bağlantıyı kapatsa da bildirim denensin diye isteğin CancellationToken'ı kullanılmaz;
// sonsuz beklememek için kendi zaman aşımı (HttpClient.Timeout, varsayılan 3 sn) vardır.
public class NotificationClient(
    HttpClient http, IOptions<InternalOptions> internalOptions, ILogger<NotificationClient> logger) : INotificationClient
{
    public const string KeyHeader = "X-Internal-Key";
    private const string Path = "/api/internal/notifications";

    public async Task SendAsync(IReadOnlyList<NotificationRequest> notifications)
    {
        var key = internalOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            // Anahtar yokken Notification zaten 401 verirdi: boşuna ağ çağrısı yapma.
            logger.LogWarning("Internal:ApiKey tanımlı değil; {Count} bildirim gönderilmedi.", notifications.Count);
            return;
        }

        // Alıcılar bağımsız: biri yavaşsa/başarısızsa diğeri beklemesin.
        await Task.WhenAll(notifications.Select(n => SendOneAsync(n, key)));
    }

    private async Task SendOneAsync(NotificationRequest notification, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path)
            {
                Content = JsonContent.Create(notification),
            };
            request.Headers.Add(KeyHeader, key);

            using var response = await http.SendAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Notification servisi {StatusCode} döndü ({Kind}).", (int)response.StatusCode, notification.Kind);
        }
        catch (Exception ex)
        {
            // Bağlantı reddi, zaman aşımı (TaskCanceledException) vb. Yalnız tür loglanır (içerik/anahtar yok).
            logger.LogWarning("Notification servisine ulaşılamadı ({Kind}): {ExceptionType}", notification.Kind, ex.GetType().Name);
        }
    }
}
