using System.Net.Http.Json;
using Hastane.Identity.Api.Settings;
using Microsoft.Extensions.Options;

namespace Hastane.Identity.Api.Integration;

public record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody);

public interface INotificationEmailClient
{
    // true: Notification kabul etti (2xx). false: ulaşılamadı/reddetti (loglandı). Asla fırlatmaz (iptal hariç).
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct);
}

// Notification servisinin iç ucu: POST /api/internal/notifications/email, X-Internal-Key başlığıyla.
// E-posta gövdesi (içinde sıfırlama token'ı var) ASLA loglanmaz; yalnız durum kodu / istisna türü.
public class NotificationEmailClient(HttpClient http, IOptions<NotificationClientOptions> options,
    ILogger<NotificationEmailClient> logger) : INotificationEmailClient
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Internal:ApiKey ayarlı değil; e-posta gönderilmedi.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/notifications/email")
            {
                Content = JsonContent.Create(new
                {
                    to = message.To,
                    subject = message.Subject,
                    textBody = message.TextBody,
                    htmlBody = message.HtmlBody,
                }),
            };
            request.Headers.Add("X-Internal-Key", apiKey);

            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;

            logger.LogWarning("Notification e-posta ucu {StatusCode} döndü.", (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Zaman aşımı (3 sn), bağlantı reddi vb. Yalnız tür loglanır.
            logger.LogWarning("Notification servisine ulaşılamadı: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
