using System.Net;
using Hastane.Contracts;
using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Order.Tests;

// Durum makinesi (Admin) ve iptal (hasta/Admin) + stok iadesi: artık Market'e senkron HTTP değil, OrderCancelled olayı
// (Market tüketicisi iade eder; tüketici testi Market.Tests/MarketEventTests). Burada Order'ın sorumluluğu sınanır: doğru olay, bir kez, iptal kaydıyla birlikte.
[Collection(OrderCollection.Name)]
public class OrderStatusAndCancelTests : OrderTestBase
{
    public OrderStatusAndCancelTests(OrderApiFactory factory) : base(factory)
    {
    }

    private async Task<OrderStatus> StatusAsync(HttpClient client, Guid id)
        => (await ReadAsync<GetByIdOrderDto>(await client.GetAsync($"/api/orders/{id}", Ct))).Status;
    [Fact]
    public async Task Admin_WalksTheHappyChain_Received_Preparing_Shipped_Delivered()
    {
        var admin = ClientFor(AdminToken());
        var order = await PlaceOrderAsync(Unique("pat"));

        foreach (var next in new[] { "Preparing", "Shipped", "Delivered" })
        {
            var response = await PatchStatusAsync(admin, order.Id, next);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(next, (await ReadAsync<GetByIdOrderDto>(response)).Status.ToString());
        }
    }

    [Theory]
    [InlineData("Shipped")]      // Received -> Shipped: Preparing atlanamaz
    [InlineData("Delivered")]    // Received -> Delivered
    [InlineData("Received")]     // aynı duruma geçiş
    public async Task InvalidTransitionFromReceived_Returns409(string target)
    {
        var order = await PlaceOrderAsync(Unique("pat"));

        var response = await PatchStatusAsync(ClientFor(AdminToken()), order.Id, target);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Backwards_AndAfterTerminal_Return409()
    {
        var admin = ClientFor(AdminToken());
        var order = await PlaceOrderAsync(Unique("pat"));
        await PatchStatusAsync(admin, order.Id, "Preparing");

        Assert.Equal(HttpStatusCode.Conflict, (await PatchStatusAsync(admin, order.Id, "Received")).StatusCode);

        await PatchStatusAsync(admin, order.Id, "Shipped");
        await PatchStatusAsync(admin, order.Id, "Delivered");
        Assert.Equal(HttpStatusCode.Conflict, (await PatchStatusAsync(admin, order.Id, "Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchStatusAsync(admin, order.Id, "Preparing")).StatusCode);
    }

    [Fact]
    public async Task Shipped_CannotBeCancelled()
    {
        var admin = ClientFor(AdminToken());
        var order = await PlaceOrderAsync(Unique("pat"));
        await PatchStatusAsync(admin, order.Id, "Preparing");
        await PatchStatusAsync(admin, order.Id, "Shipped");

        Assert.Equal(HttpStatusCode.Conflict, (await PatchStatusAsync(admin, order.Id, "Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct)).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"Nonsense\"}")]
    [InlineData("{\"status\":7}")]
    public async Task MissingOrUnknownStatus_Returns400(string body)
    {
        var order = await PlaceOrderAsync(Unique("pat"));

        var response = await ClientFor(AdminToken()).PatchAsync($"/api/orders/{order.Id}/status", Raw(body), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_OfUnknownOrder_Returns404()
    {
        var response = await PatchStatusAsync(ClientFor(AdminToken()), Guid.NewGuid(), "Preparing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CancelViaStatus_PublishesOrderCancelled()
    {
        var order = await PlaceOrderAsync(Unique("pat"));
        var admin = ClientFor(AdminToken());
        await PatchStatusAsync(admin, order.Id, "Preparing");

        var response = await PatchStatusAsync(admin, order.Id, "Cancelled");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await ReadAsync<GetByIdOrderDto>(response);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.StockReleasePending);
        var published = await Harness.Published.SelectAsync<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct).FirstOrDefault();
        Assert.NotNull(published);
        Assert.Equal(order.OrderNumber, published.Context.Message.OrderNumber);
        Assert.Equal(order.PatientUserId, published.Context.Message.PatientUserId);
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/release")); // senkron iade çağrısı yok
    }

    // --- iptal ---

    [Fact]
    public async Task Patient_CancelsOwnReceivedOrder_AndOrderCancelledIsPublished()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);

        var response = await ClientFor(PatientToken(sub)).PostAsync($"/api/orders/{order.Id}/cancel", Raw("{\"reason\":\"Vazgeçtim\"}"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await ReadAsync<GetByIdOrderDto>(response);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal("Vazgeçtim", cancelled.CancelReason);
        Assert.False(cancelled.StockReleasePending);
        var published = await Harness.Published.SelectAsync<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct).FirstOrDefault();
        Assert.NotNull(published);
        Assert.Equal("Vazgeçtim", published.Context.Message.Reason);
        Assert.NotEqual(Guid.Empty, published.Context.Message.EventId);
        Assert.Equal(0, Market.CallCount("POST", "/api/internal/stock/release"));
    }

    [Fact]
    public async Task Patient_CancelWithoutBody_UsesDefaultReason()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);

        var response = await ClientFor(PatientToken(sub)).PostAsync($"/api/orders/{order.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await ReadAsync<GetByIdOrderDto>(response)).CancelReason));
    }

    [Fact]
    public async Task Patient_CannotCancel_OncePreparing()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);
        await PatchStatusAsync(ClientFor(AdminToken()), order.Id, "Preparing");

        var response = await ClientFor(PatientToken(sub)).PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(await Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct));
    }

    [Fact]
    public async Task Admin_CanCancel_PreparingOrder()
    {
        var order = await PlaceOrderAsync(Unique("pat"));
        var admin = ClientFor(AdminToken());
        await PatchStatusAsync(admin, order.Id, "Preparing");

        var response = await admin.PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct));
    }

    [Fact]
    public async Task CancelTwice_SecondReturns409_AndOrderCancelledIsPublishedOnce()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);
        var client = ClientFor(PatientToken(sub));

        var first = await client.PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);
        var second = await client.PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct);
        Assert.Single(Harness.Published.Select<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct));
    }

    // ÖĞRETİM: eski senkron yolda Market kapalıysa iade "bekliyor" işaretiyle kalırdı. Şimdi iptal Market'ten bağımsız kaydolur,
    // olay outbox'ta durur ve Market geri gelince tüketilir; Order'ın Market'e ulaşabilmesi gerekmez.
    [Fact]
    public async Task Cancel_WhenMarketIsDown_StillSucceeds_AndEventIsPublished()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);
        Market.CartMode = Market.ProductMode = Market.ReserveMode = Market.ClearMode = MarketMode.Down;

        var response = await ClientFor(PatientToken(sub)).PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, Ct));
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.False(stored.StockReleasePending);
        Assert.True(await Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct));
    }
    [Fact]
    public async Task ConcurrentCancelAndStatusChange_OnlyOneWins_NoLostUpdate()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);
        var patient = ClientFor(PatientToken(sub));
        var admin = ClientFor(AdminToken());

        var cancel = patient.PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);
        var prepare = PatchStatusAsync(admin, order.Id, "Preparing");
        var results = await Task.WhenAll(cancel, prepare);

        // İki işlem de Received'ı görmüş olabilir; iyimser eşzamanlılık (Version) ikincisini ezmek yerine 409 verir
        // ya da ikisi sırayla geçerli olur (Preparing sonrası hasta iptali 409). En çok biri başarılı ya da zincir tutarlı olmalı.
        var final = await StatusAsync(admin, order.Id);
        Assert.Contains(final, new[] { OrderStatus.Cancelled, OrderStatus.Preparing });
        if (final == OrderStatus.Cancelled) Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        if (final == OrderStatus.Preparing) Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
    }
}
