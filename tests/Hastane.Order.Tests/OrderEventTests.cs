using System.Net;
using Hastane.Contracts;
using Hastane.Order.Api.Dtos;
using Hastane.TestSupport;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Order.Tests;

// Order -> olay yayını. Outbox GERÇEK MariaDB üzerinde (Oracle MySql.EntityFrameworkCore + MassTransit EF outbox "UseMySql"):
// olay OutboxMessage tablosuna yazılır, teslim servisi bellek içi bus'a (test harness) taşır.
[Collection(OrderCollection.Name)]
public class OrderEventTests : OrderTestBase
{
    public OrderEventTests(OrderApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Checkout_PublishesOrderPlaced_WithMinimalFields()
    {
        var product = Market.AddProduct("Tansiyon aleti", 250.50m);
        var (client, sub) = PatientWithCart((product, 2));

        var order = await PlaceOrderAsync(client);

        var published = await Harness.ConsumedEventAsync<OrderPlaced>(m => m.Context.Message.OrderId == order.Id, Ct);
        Assert.NotNull(published);
        var e = published.Context.Message;
        Assert.NotEqual(Guid.Empty, e.EventId);
        Assert.Equal(order.OrderNumber, e.OrderNumber);
        Assert.Equal(sub, e.PatientUserId);
        Assert.Equal(501.00m, e.TotalAmount);
        Assert.Equal("TRY", e.Currency);
    }

    [Fact]
    public async Task Checkout_Replay_WithSameIdempotencyKey_PublishesOnlyOneOrderPlaced()
    {
        var product = Market.AddProduct("Termometre", 100m);
        var (client, _) = PatientWithCart((product, 1));
        var key = Unique("key");

        var first = await CheckoutAsync(client, key: key);
        var second = await CheckoutAsync(client, key: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var orderId = (await ReadAsync<GetByIdOrderDto>(first)).Id;
        await Harness.AnyConsumedAsync<OrderPlaced>(m => m.Context.Message.OrderId == orderId, Ct);
        await OutboxHarnessExtensions.WaitQuietPeriodAsync(Ct);
        Assert.Single(Harness.Consumed.Select<OrderPlaced>(m => m.Context.Message.OrderId == orderId, Ct));
    }

    [Fact]
    public async Task EachOrderEvent_HasItsOwnEventId()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);
        var cancel = await ClientFor(PatientToken(sub)).PostAsync($"/api/orders/{order.Id}/cancel", Raw("{}"), Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var placed = await Harness.ConsumedEventAsync<OrderPlaced>(m => m.Context.Message.OrderId == order.Id, Ct);
        var cancelled = await Harness.ConsumedEventAsync<OrderCancelled>(m => m.Context.Message.OrderId == order.Id, Ct);
        Assert.NotNull(placed);
        Assert.NotNull(cancelled);
        Assert.NotEqual(placed.Context.Message.EventId, cancelled.Context.Message.EventId);
    }

    [Fact]
    public async Task OutboxTables_ExistAfterMigration()
    {
        var count = await WithDbAsync(async db => await db.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ('OutboxMessage','OutboxState','InboxState')")
            .SingleAsync(Ct));
        Assert.Equal(3, count);
    }
}
