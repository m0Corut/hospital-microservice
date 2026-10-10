using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hastane.Order.Api.Settings;
using Microsoft.Extensions.Options;

namespace Hastane.Order.Api.Integration;

// Market servisinin (5109) typed HttpClient'ı. Sözleşme: ortak brif. Market koduna bağımlılık yok.
public class MarketClient(
    HttpClient http,
    IHttpContextAccessor accessor,
    IOptions<InternalOptions> internalOptions,
    ILogger<MarketClient> logger) : IMarketClient
{
    public const string KeyHeader = "X-Internal-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MarketResult<CartInfo>> GetCartAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        ForwardAuthorization(request);
        return await SendForJsonAsync<CartInfo>(request, "GET /api/cart", ct);
    }

    public async Task<MarketResult<ProductInfo>> GetProductAsync(Guid productId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId:D}");
        return await SendForJsonAsync<ProductInfo>(request, "GET /api/products/{id}", ct);
    }

    public async Task<ReserveOutcome> ReserveAsync(Guid orderId, IReadOnlyList<ReserveItem> items, CancellationToken ct)
    {
        if (!TryInternalRequest(HttpMethod.Post, "/api/internal/stock/reserve",
                JsonContent.Create(new { orderId, items }), out var request))
            return ReserveOutcome.Unavailable;

        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return ReserveOutcome.Reserved;
                if (response.StatusCode == HttpStatusCode.Conflict) return ReserveOutcome.InsufficientStock;
                // 401 (anahtar uyumsuzluğu), 5xx, beklenmeyen 4xx: stok durumu bilinmiyor.
                logger.LogWarning("Market stok ayırma {StatusCode} döndü (sipariş {OrderId}).", (int)response.StatusCode, orderId);
                return ReserveOutcome.Unavailable;
            }
            catch (Exception ex) when (IsTransport(ex, ct))
            {
                logger.LogWarning("Market stok ayırma çağrısı başarısız (sipariş {OrderId}): {ExceptionType}", orderId, ex.GetType().Name);
                return ReserveOutcome.Unavailable;
            }
        }
    }

    public async Task<bool> ClearCartAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/cart");
        ForwardAuthorization(request);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;
            logger.LogWarning("Market sepet boşaltma {StatusCode} döndü.", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            logger.LogWarning("Market sepet boşaltma çağrısı başarısız: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private void ForwardAuthorization(HttpRequestMessage request)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header))
            request.Headers.Authorization = header;
    }

    private bool TryInternalRequest(HttpMethod method, string path, HttpContent content, out HttpRequestMessage request)
    {
        var key = internalOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            // Anahtar yokken Market zaten 401 verirdi: boşuna ağ çağrısı yapma.
            logger.LogWarning("Internal:ApiKey tanımlı değil; Market iç ucu ({Path}) çağrılamadı.", path);
            request = null!;
            return false;
        }

        request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(KeyHeader, key);
        return true;
    }

    private async Task<MarketResult<T>> SendForJsonAsync<T>(HttpRequestMessage request, string what, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return MarketResult<T>.NotFound();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Market {What} için {StatusCode} döndü.", what, (int)response.StatusCode);
                return MarketResult<T>.Unavailable();
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return value is null ? MarketResult<T>.Unavailable() : MarketResult<T>.Ok(value);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            // Zaman aşımı (HttpClient.Timeout => TaskCanceledException), bağlantı reddi, bozuk JSON. Yalnız tür loglanır.
            logger.LogWarning("Market'e ulaşılamadı ({What}): {ExceptionType}", what, ex.GetType().Name);
            return MarketResult<T>.Unavailable();
        }
    }

    // İstemci vazgeçtiyse (ct iptal) bu bir Market hatası değil: istisna yukarı çıksın.
    private static bool IsTransport(Exception ex, CancellationToken ct)
        => !(ex is OperationCanceledException && ct.IsCancellationRequested)
           && ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException;
}
