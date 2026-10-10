using System.Net;
using System.Text.RegularExpressions;
using Hastane.Contracts;
using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;
using Hastane.Order.Api.Services;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Order.Tests;

// Checkout sırası: sepeti oku -> ürünleri yeniden oku -> siparişi Received yaz -> stok ayır -> sepeti boşalt.
[Collection(OrderCollection.Name)]
public class CheckoutTests : OrderTestBase
{
    public CheckoutTests(OrderApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task HappyPath_PricesComeFromProductService_NotFromCart()
    {
        var product = Market.AddProduct("Tansiyon aleti", 250.50m);
        var sub = Unique("pat");
        // Sepetteki fiyat bilerek eski/oynanmış (1 TL): sunucu doğrusu ürün servisindeki 250.50 olmalı.
        Market.SetCart(sub, new FakeCartLine(product.Id, product.Name, 1m, 2));
        var client = ClientFor(PatientToken(sub, "Mehmet Kaya"));

        var response = await CheckoutAsync(client, NewCheckout(note: "Zili çalmayın"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var order = await ReadAsync<GetByIdOrderDto>(response);
        Assert.Matches(new Regex(@"^SIP-\d{4}-\d{6}$"), order.OrderNumber);
        Assert.Equal(OrderStatus.Received, order.Status);
        Assert.Equal(sub, order.PatientUserId);
        Assert.Equal("Mehmet Kaya", order.PatientName); // token'daki `name`
        Assert.Equal("CashOnDelivery", order.PaymentMethod);
        Assert.Equal("TRY", order.Currency);
        Assert.Equal(501.00m, order.TotalAmount);
        Assert.Equal("Zili çalmayın", order.Note);
        var item = Assert.Single(order.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(250.50m, item.UnitPrice);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(501.00m, item.LineTotal);

        // Market sözleşmesi: reserve iç anahtarla ve sipariş kimliğiyle çağrıldı, sonra sepet kullanıcının token'ıyla boşaltıldı.
        var reserve = Assert.Single(Market.Calls, c => c.Path == "/api/internal/stock/reserve");
        Assert.Equal(FakeMarket.InternalKey, reserve.InternalKey);
        Assert.Contains(order.Id.ToString(), reserve.Body);
        Assert.Equal(1, Market.CallCount("DELETE", "/api/cart"));
        Assert.True(Market.CartIsEmpty(sub));
        Assert.All(Market.Calls.Where(c => c.Path == "/api/cart"), c => Assert.StartsWith("Bearer ", c.Authorization));
    }

    [Fact]
    public async Task Order_IsASnapshot_LaterPriceChangeDoesNotAffectIt()
    {
        var product = Market.AddProduct("Ateş ölçer", 100m);
        var (client, _) = PatientWithCart((product, 1));
        var placed = await PlaceOrderAsync(client);

        Market.SetProduct(product with { Price = 999m, Name = "Yeni ad" });
        var again = await ReadAsync<GetByIdOrderDto>(await client.GetAsync($"/api/orders/{placed.Id}", Ct));

        Assert.Equal(100m, again.Items[0].UnitPrice);
        Assert.Equal("Ateş ölçer", again.Items[0].ProductName);
        Assert.Equal(100m, again.TotalAmount);
    }

    [Fact]
    public async Task EmptyCart_Returns400_AndWritesNothing()
    {
        var (client, sub) = PatientWithCart();

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/reserve"));
    }

    [Fact]
    public async Task InactiveProduct_Returns409_AndWritesNothing()
    {
        var product = Market.AddProduct("Pasif ürün", 50m, isActive: false);
        var (client, sub) = PatientWithCart((product, 1));

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/reserve"));
    }

    [Fact]
    public async Task MissingProduct_Returns409()
    {
        var sub = Unique("pat");
        Market.SetCart(sub, new FakeCartLine(Guid.CreateVersion7(), "Silinmiş", 10m, 1));

        var response = await CheckoutAsync(ClientFor(PatientToken(sub)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task InsufficientStock_MarksOrderCancelled_AndReturns409()
    {
        var product = Market.AddProduct("Maske", 5m);
        var (client, sub) = PatientWithCart((product, 3));
        Market.InsufficientStock = true;

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var order = Assert.Single(await WithDbAsync(db => db.Orders.Where(o => o.PatientUserId == sub).ToListAsync(Ct)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Stok yetersiz", order.CancelReason);
        Assert.False(order.StockReleasePending);
        Assert.Equal(0, Market.CallCount("DELETE", "/api/cart")); // sepet korunur: hasta miktarı düzeltip tekrar deneyebilir
        Assert.False(Market.CartIsEmpty(sub));
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/release")); // Market hiçbir şey düşmedi
        // Stok hiç ayrılmadığı için iade olayı da yok (ve sipariş "alındı" sayılmadığından OrderPlaced da yok).
        Assert.False(await Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct));
        Assert.False(await Harness.Published.Any<OrderPlaced>(m => m.Context.Message.OrderId == order.Id, Ct));
    }

    [Theory]
    [InlineData(MarketMode.Down)]
    [InlineData(MarketMode.Timeout)]
    [InlineData(MarketMode.Error500)]
    public async Task CartUnavailable_Returns503_AndWritesNothing(MarketMode mode)
    {
        var product = Market.AddProduct("Eldiven", 20m);
        var (client, sub) = PatientWithCart((product, 1));
        Market.CartMode = mode;

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task ProductServiceUnavailable_Returns503_AndWritesNothing()
    {
        var product = Market.AddProduct("Eldiven", 20m);
        var (client, sub) = PatientWithCart((product, 1));
        Market.ProductMode = MarketMode.Down;

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    [Theory]
    [InlineData(MarketMode.Down)]
    [InlineData(MarketMode.Timeout)]
    public async Task ReserveUnavailable_Returns503_OrderCancelled_AndCancelledEventPublishedForRelease(MarketMode mode)
    {
        var product = Market.AddProduct("Gazlı bez", 15m);
        var (client, sub) = PatientWithCart((product, 1));
        Market.ReserveMode = mode;

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var order = Assert.Single(await WithDbAsync(db => db.Orders.Where(o => o.PatientUserId == sub).ToListAsync(Ct)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.False(order.StockReleasePending); // eski "iade bekliyor" işareti yok: iade olayla, outbox garantisiyle
        // Reserve yanıtı kaybolmuş olabilir (Market stoğu düşmüş olabilir): OrderCancelled yayınlanır, Market idempotent iade eder.
        var published = await Harness.Published.SelectAsync<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct).FirstOrDefault();
        Assert.NotNull(published);
        Assert.Equal(OrderService.ReserveUnavailableReason, published.Context.Message.Reason);
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/release")); // senkron iade çağrısı artık yok
        Assert.False(Market.CartIsEmpty(sub));
    }

    [Fact]
    public async Task ClearCartFails_OrderIsStillSuccessful()
    {
        var product = Market.AddProduct("Pamuk", 8m);
        var (client, sub) = PatientWithCart((product, 1));
        Market.ClearMode = MarketMode.Error500;

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(OrderStatus.Received, (await ReadAsync<GetByIdOrderDto>(response)).Status);
        Assert.Equal(1, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task CurrencyMismatchInCart_Returns409()
    {
        var a = Market.AddProduct("A", 10m);
        var b = Market.AddProduct("B", 10m, currency: "USD");
        var (client, sub) = PatientWithCart((a, 1), (b, 1));

        var response = await CheckoutAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    // --- doğrulama ---

    [Fact]
    public async Task MissingOrTooLongFields_Return400()
    {
        var product = Market.AddProduct("Maske", 5m);
        var (client, sub) = PatientWithCart((product, 1));

        Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutAsync(client, NewCheckout(address: ""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutAsync(client, NewCheckout(address: new string('a', 301)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutAsync(client, NewCheckout(note: new string('n', 501)))).StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task ClientCannotInjectOwnerPriceOrStatus_ExtraBodyFieldsAreIgnored()
    {
        var product = Market.AddProduct("Maske", 5m);
        var (client, sub) = PatientWithCart((product, 1));

        var response = await client.PostAsync("/api/orders/checkout", Raw(
            "{\"shippingAddress\":\"Adres satırı 1\",\"patientUserId\":\"baskasi\",\"totalAmount\":0.01,\"status\":\"Delivered\"}"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await ReadAsync<GetByIdOrderDto>(response);
        Assert.Equal(sub, order.PatientUserId);
        Assert.Equal(5m, order.TotalAmount);
        Assert.Equal(OrderStatus.Received, order.Status);
    }

    // --- Idempotency-Key ---

    [Fact]
    public async Task SameIdempotencyKey_ReturnsSameOrder_AndReservesOnce()
    {
        var product = Market.AddProduct("Serum", 40m);
        var (client, sub) = PatientWithCart((product, 1));
        var key = Unique("key");

        var first = await CheckoutAsync(client, key: key);
        // Sepet ilk istekte boşaltıldı; çift tıklamanın ikinci isteği boş sepet 400'ü DEĞİL, ilk siparişi görmeli.
        var second = await CheckoutAsync(client, key: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await ReadAsync<GetByIdOrderDto>(first)).Id, (await ReadAsync<GetByIdOrderDto>(second)).Id);
        Assert.Equal(1, await CountOrdersOfAsync(sub));
        Assert.Equal(1, Market.CallCount("POST", "/api/internal/stock/reserve"));
    }

    [Fact]
    public async Task SameIdempotencyKey_AfterStockFailure_ReplaysTheSameConflict()
    {
        var product = Market.AddProduct("Serum", 40m);
        var (client, sub) = PatientWithCart((product, 1));
        var key = Unique("key");
        Market.InsufficientStock = true;

        var first = await CheckoutAsync(client, key: key);
        Market.InsufficientStock = false; // stok geri gelse bile aynı anahtar yeni sipariş açmaz
        var second = await CheckoutAsync(client, key: key);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task SameKey_FromDifferentPatients_CreatesSeparateOrders()
    {
        var product = Market.AddProduct("Serum", 40m);
        var (c1, s1) = PatientWithCart((product, 1));
        var (c2, s2) = PatientWithCart((product, 1));
        var key = Unique("key");

        var r1 = await CheckoutAsync(c1, key: key);
        var r2 = await CheckoutAsync(c2, key: key);

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.NotEqual((await ReadAsync<GetByIdOrderDto>(r1)).Id, (await ReadAsync<GetByIdOrderDto>(r2)).Id);
        Assert.Equal(1, await CountOrdersOfAsync(s1));
        Assert.Equal(1, await CountOrdersOfAsync(s2));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("kisa-anahtar")]
    [InlineData("bosluk iceren anahtar degeri 123")]
    public async Task InvalidIdempotencyKey_Returns400(string key)
    {
        var product = Market.AddProduct("Serum", 40m);
        var (client, sub) = PatientWithCart((product, 1));

        var response = await CheckoutAsync(client, key: key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountOrdersOfAsync(sub));
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_CreateExactlyOneOrder()
    {
        var product = Market.AddProduct("Serum", 40m);
        var (client, sub) = PatientWithCart((product, 1));
        var key = Unique("key");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CheckoutAsync(client, key: key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"beklenmeyen {r.StatusCode}"));
        var ids = (await Task.WhenAll(responses.Select(ReadAsync<GetByIdOrderDto>))).Select(o => o.Id).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await CountOrdersOfAsync(sub));
        Assert.Single(Market.ReservedOrders);
    }

    // --- sipariş numarası ---

    [Fact]
    public async Task OrderNumbers_AreUnique_UnderTenConcurrentCheckouts()
    {
        var product = Market.AddProduct("Maske", 5m);
        var clients = Enumerable.Range(0, 10).Select(_ => PatientWithCart((product, 1)).Client).ToList();

        var responses = await Task.WhenAll(clients.Select(c => CheckoutAsync(c)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = (await Task.WhenAll(responses.Select(ReadAsync<GetByIdOrderDto>))).Select(o => o.OrderNumber).ToList();
        Assert.Equal(10, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(@"^SIP-\d{4}-\d{6}$", n));
    }

    [Fact]
    public async Task OrderNumbers_AreSequential_WithinTheYear()
    {
        var product = Market.AddProduct("Maske", 5m);
        var (c1, _) = PatientWithCart((product, 1));
        var (c2, _) = PatientWithCart((product, 1));

        var a = await PlaceOrderAsync(c1);
        var b = await PlaceOrderAsync(c2);

        var seqA = int.Parse(a.OrderNumber[^6..]);
        var seqB = int.Parse(b.OrderNumber[^6..]);
        Assert.Equal(seqA + 1, seqB);
    }
}
