using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hastane.Chat.Api.Integration;

// Arama istemcilerinin ortak işi: gelen isteğin Authorization başlığını AYNEN iletmek, GET atmak, hataları sınıflamak.
// Yetki kararı karşı serviste verilir (Appointment listeyi token kapsamına göre süzer): Chat kuralı yeniden yazmaz.
internal static class DownstreamClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<LookupResult<T>> GetAsync<T>(
        HttpClient http, string path, IHttpContextAccessor accessor, ILogger logger, CancellationToken ct) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header))
            request.Headers.Authorization = header;

        try
        {
            using var response = await http.SendAsync(request, ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound: return LookupResult<T>.NotFound();
                case HttpStatusCode.Forbidden: return LookupResult<T>.Forbidden();
            }

            if (!response.IsSuccessStatusCode)
            {
                // 5xx ya da beklenmeyen 4xx (ör. 401: servisler arası anahtar/ayar uyumsuzluğu): hepsi "doğrulanamadı".
                logger.LogWarning("Aşağı akış servisi {Path} için {StatusCode} döndü.", path, (int)response.StatusCode);
                return LookupResult<T>.Unavailable();
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return value is null ? LookupResult<T>.Unavailable() : LookupResult<T>.Found(value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // istemci vazgeçti: bu bir servis hatası değil
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            // Zaman aşımı (HttpClient.Timeout => TaskCanceledException), bağlantı reddi, bozuk JSON. Yalnız tür loglanır.
            logger.LogWarning("Aşağı akış servisine ulaşılamadı ({Path}): {ExceptionType}", path, ex.GetType().Name);
            return LookupResult<T>.Unavailable();
        }
    }
}
