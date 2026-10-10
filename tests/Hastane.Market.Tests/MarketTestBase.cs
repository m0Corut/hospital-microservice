using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Market.Api.Data;
using Hastane.Market.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Market.Tests;

// Ortak yardımcılar. Factory'yi (SQL + Redis) alt sınıflar verir; bu sınıf yalnız HTTP/veri kurulum kolaylığıdır.
public abstract class MarketTestBase<TFactory> where TFactory : MarketApiFactory
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected readonly TFactory Factory;

    protected MarketTestBase(TFactory factory)
    {
        Factory = factory;
        Admin = factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
        Anonymous = factory.CreateClient();
    }

    protected HttpClient Admin { get; }

    protected HttpClient Anonymous { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected HttpClient ClientFor(string? token)
    {
        var client = Factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    protected HttpClient PatientClient(string sub) => ClientFor(PatientToken(sub));

    protected static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);

    protected static string DoctorToken() => TestJwt.Create(Unique("doc"), [HastaneRoles.Doctor], Unique("did"));

    protected static object ProductBody(
        string? name = null, decimal price = 100m, int stock = 10, bool isActive = true,
        string category = "Vitamin", string? description = "Açıklama", string? imageUrl = "/img/products/vitamin-c.svg")
        => new { name = name ?? Unique("Ürün"), description, category, price, stock, imageUrl, isActive };

    protected async Task<ResultProductDto> CreateProductAsync(
        string? name = null, decimal price = 100m, int stock = 10, bool isActive = true, string category = "Vitamin")
    {
        var response = await Admin.PostAsJsonAsync("/api/products", ProductBody(name, price, stock, isActive, category), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ResultProductDto>(response);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    // Yönetici GET'i önbelleğe hiç uğramaz: veritabanındaki gerçek hali verir.
    protected async Task<ResultProductDto> GetTruthAsync(Guid id)
    {
        var response = await Admin.GetAsync($"/api/products/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<ResultProductDto>(response);
    }

    protected async Task<int> StockOfAsync(Guid id) => (await GetTruthAsync(id)).Stock;

    protected async Task<T> WithDbAsync<T>(Func<MarketDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<MarketDbContext>());
    }

    // İç uç isteği: anahtar başlığı verilmezse geçerli test anahtarı eklenir; null verilirse başlık hiç eklenmez.
    protected static HttpRequestMessage InternalRequest(string url, object body, string? key = MarketApiFactory.TestInternalKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };
        if (key is not null) request.Headers.TryAddWithoutValidation("X-Internal-Key", key);
        return request;
    }

    protected static object ReserveBody(Guid orderId, params (Guid ProductId, int Quantity)[] items)
        => new { orderId, items = items.Select(i => new { productId = i.ProductId, quantity = i.Quantity }).ToArray() };

    protected Task<HttpResponseMessage> ReserveAsync(Guid orderId, params (Guid ProductId, int Quantity)[] items)
        => Anonymous.SendAsync(InternalRequest("/api/internal/stock/reserve", ReserveBody(orderId, items)), Ct);

    protected Task<HttpResponseMessage> ReleaseAsync(Guid orderId)
        => Anonymous.SendAsync(InternalRequest("/api/internal/stock/release", new { orderId }), Ct);

    protected static async Task<JsonElement> JsonBodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
}

// Paylaşılan factory ile çalışan testlerin ortak tabanı.
public abstract class MarketTestBase : MarketTestBase<MarketApiFactory>
{
    protected MarketTestBase(MarketApiFactory factory) : base(factory)
    {
    }
}
