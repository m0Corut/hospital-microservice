using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Order.Api.Data;
using Hastane.Order.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Order.Tests;

public abstract class OrderTestBase : IDisposable
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly OrderApiFactory Factory;
    protected FakeMarket Market => Factory.Market;

    protected OrderTestBase(OrderApiFactory factory)
    {
        Factory = factory;
        Market.Reset(); // testler seri; her test temiz bir sahte Market ile başlar
    }

    public void Dispose() => Market.Reset();

    // MassTransit test harness: Order'ın yayınladığı olaylar burada görünür (outbox -> teslim servisi -> bellek içi bus).
    protected ITestHarness Harness => Factory.Services.GetTestHarness();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected static string PatientToken(string sub, string name = "Ayşe Yılmaz")
        => TestJwt.Create(sub, [HastaneRoles.Patient], name: name);

    protected static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);
    protected static string DoctorToken() => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], Unique("doc"));

    protected HttpClient ClientFor(string? token = null)
    {
        var client = Factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    // Hasta + sepet kurar: her satır (ürün, adet, sepetteki eski fiyat). Dönen istemci o hastanın token'ıyla konuşur.
    protected (HttpClient Client, string Sub) PatientWithCart(params (FakeProduct Product, int Quantity)[] lines)
    {
        var sub = Unique("pat");
        Market.SetCart(sub, lines.Select(l => new FakeCartLine(l.Product.Id, l.Product.Name, l.Product.Price, l.Quantity)).ToArray());
        return (ClientFor(PatientToken(sub)), sub);
    }

    protected static CheckoutDto NewCheckout(string address = "Atatürk Cad. No:1 Kadıköy/İstanbul", string? note = null)
        => new() { ShippingAddress = address, Note = note };

    protected static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, CheckoutDto? dto = null, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders/checkout")
        {
            Content = JsonContent.Create(dto ?? NewCheckout(), options: Json),
        };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request, Ct);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    // Başarılı bir sipariş verir ve ayrıntısını döndürür.
    protected async Task<GetByIdOrderDto> PlaceOrderAsync(HttpClient client)
    {
        var response = await CheckoutAsync(client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<GetByIdOrderDto>(response);
    }

    protected async Task<GetByIdOrderDto> PlaceOrderAsync(string sub)
    {
        var product = Market.AddProduct("Termometre", 100m);
        Market.SetCart(sub, new FakeCartLine(product.Id, product.Name, product.Price, 1));
        return await PlaceOrderAsync(ClientFor(PatientToken(sub)));
    }

    protected static StringContent Raw(string json)
        => new(json, System.Text.Encoding.UTF8, "application/json");

    protected Task<HttpResponseMessage> PatchStatusAsync(HttpClient client, Guid id, string status)
        => client.PatchAsync($"/api/orders/{id}/status", Raw($"{{\"status\":\"{status}\"}}"), Ct);

    // Doğrudan veritabanına bakmak için kısa ömürlü bir kapsam.
    protected async Task<T> WithDbAsync<T>(Func<OrderDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderDbContext>());
    }

    protected Task<int> CountOrdersOfAsync(string sub)
        => WithDbAsync(db => db.Orders.CountAsync(o => o.PatientUserId == sub, Ct));
}
