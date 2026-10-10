using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Order.Tests;

public enum MarketMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
}

public sealed record FakeProduct(Guid Id, string Name, decimal Price, string Currency = "TRY", bool IsActive = true, int Stock = 100);

// Sepet satırı: CartPrice bilerek ürün servisindeki fiyattan farklı olabilir (eski/oynanmış sepet fiyatı yok sayılmalı).
public sealed record FakeCartLine(Guid ProductId, string Name, decimal CartPrice, int Quantity);

public sealed record MarketCall(string Method, string Path, string? Authorization, string? InternalKey, string? Body);

// Market servisinin (5109) sözleşmeye göre sahte hali: gerçek ağ yok. Order'ın typed HttpClient'ının altına
// HttpMessageHandler olarak takılır. Sepeti isteğin Bearer token'ındaki `sub` ile bulur (kullanıcıya özel sepet).
public sealed class FakeMarket
{
    public const string InternalKey = "test-internal-key";

    private readonly ConcurrentDictionary<string, List<FakeCartLine>> _carts = new();
    private readonly ConcurrentDictionary<Guid, FakeProduct> _products = new();
    private readonly ConcurrentQueue<MarketCall> _calls = new();
    private readonly ConcurrentDictionary<Guid, byte> _reserved = new();

    public MarketMode CartMode { get; set; } = MarketMode.Normal;
    public MarketMode ProductMode { get; set; } = MarketMode.Normal;
    public MarketMode ReserveMode { get; set; } = MarketMode.Normal;
    public MarketMode ReleaseMode { get; set; } = MarketMode.Normal;
    public MarketMode ClearMode { get; set; } = MarketMode.Normal;

    // true: stok yetersiz, reserve 409 döner ve hiçbir şey düşmez.
    public bool InsufficientStock { get; set; }

    // Gerçek Market ile aynı: aynı orderId ikinci kez gelirse 200 (idempotent).
    public IReadOnlyCollection<Guid> ReservedOrders => _reserved.Keys.ToArray();
    public IReadOnlyList<MarketCall> Calls => _calls.ToArray();

    public void Reset()
    {
        CartMode = ProductMode = ReserveMode = ReleaseMode = ClearMode = MarketMode.Normal;
        InsufficientStock = false;
        _reserved.Clear();
        while (_calls.TryDequeue(out _))
        {
        }
    }

    public FakeProduct AddProduct(string name, decimal price, bool isActive = true, string currency = "TRY")
    {
        var product = new FakeProduct(Guid.CreateVersion7(), name, price, currency, isActive);
        _products[product.Id] = product;
        return product;
    }

    public void SetProduct(FakeProduct product) => _products[product.Id] = product;

    public void SetCart(string userId, params FakeCartLine[] lines) => _carts[userId] = [.. lines];

    public bool CartIsEmpty(string userId) => !_carts.TryGetValue(userId, out var lines) || lines.Count == 0;

    public int CallCount(string method, string pathPrefix)
        => Calls.Count(c => c.Method == method && c.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FakeMarket market) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            market._calls.Enqueue(new MarketCall(
                request.Method.Method, path, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Internal-Key", out var k) ? k.First() : null, body));

            if (path == "/api/cart") return await Gate(market.CartMode, ct) ?? Cart(request);
            if (path.StartsWith("/api/products/", StringComparison.Ordinal)) return await Gate(market.ProductMode, ct) ?? Product(path);
            if (path == "/api/internal/stock/reserve") return await Gate(market.ReserveMode, ct) ?? Reserve(request, body);
            if (path == "/api/internal/stock/release") return await Gate(market.ReleaseMode, ct) ?? Release(request);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static async Task<HttpResponseMessage?> Gate(MarketMode mode, CancellationToken ct)
        {
            switch (mode)
            {
                case MarketMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case MarketMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    return null;
                case MarketMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                default: return null;
            }
        }

        private HttpResponseMessage Cart(HttpRequestMessage request)
        {
            var sub = SubOf(request.Headers.Authorization?.Parameter);
            if (sub is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            if (request.Method == HttpMethod.Delete)
            {
                if (market.ClearMode != MarketMode.Normal) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                market._carts[sub] = [];
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var lines = market._carts.GetValueOrDefault(sub) ?? [];
            var items = lines.Select(l => new
            {
                productId = l.ProductId,
                name = l.Name,
                unitPrice = l.CartPrice,
                quantity = l.Quantity,
                lineTotal = l.CartPrice * l.Quantity,
            }).ToList();
            return Json(new { items, total = items.Sum(i => i.lineTotal), currency = "TRY" });
        }

        private HttpResponseMessage Product(string path)
        {
            var id = path[(path.LastIndexOf('/') + 1)..];
            if (!Guid.TryParse(id, out var guid) || !market._products.TryGetValue(guid, out var p))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(new { id = p.Id, name = p.Name, description = "", category = "Genel", price = p.Price, currency = p.Currency, stock = p.Stock, imageUrl = (string?)null, isActive = p.IsActive });
        }

        private HttpResponseMessage Reserve(HttpRequestMessage request, string? body)
        {
            // Sözleşme: X-Internal-Key yoksa/yanlışsa 401.
            if (!request.Headers.TryGetValues("X-Internal-Key", out var keys) || keys.First() != InternalKey)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            using var doc = JsonDocument.Parse(body!);
            var orderId = doc.RootElement.GetProperty("orderId").GetGuid();
            if (market._reserved.ContainsKey(orderId)) return new HttpResponseMessage(HttpStatusCode.OK);
            if (market.InsufficientStock)
            {
                var first = doc.RootElement.GetProperty("items")[0].GetProperty("productId").GetGuid();
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { productId = first, title = "Stok yetersiz" }), Encoding.UTF8, "application/json"),
                };
            }
            market._reserved[orderId] = 0;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private HttpResponseMessage Release(HttpRequestMessage request)
        {
            if (!request.Headers.TryGetValues("X-Internal-Key", out var keys) || keys.First() != InternalKey)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
        };

        // İmza doğrulamaz (sahte servis); yalnız yük kısmından `sub` okur.
        private static string? SubOf(string? token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("sub", out var s) ? s.GetString() : null;
        }
    }
}
