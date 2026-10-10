using System.Net;
using Hastane.Contracts;
using MassTransit;
using MassTransit.Testing;

namespace Hastane.Market.Tests;

// OrderCancelled olayı -> stok iadesi (Order artık senkron release çağırmıyor). Taşıma: MassTransit test harness.
[Collection(MarketCollection.Name)]
public class MarketEventTests : MarketTestBase
{
    public MarketEventTests(MarketApiFactory factory) : base(factory)
    {
    }

    private ITestHarness Harness => Factory.Services.GetTestHarness();

    private static OrderCancelled Cancelled(Guid orderId) => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = orderId,
        OrderNumber = "SIP-2026-000001",
        PatientUserId = "pat-1",
        Reason = "Hasta tarafından iptal edildi",
    };

    // Tüketici olayı n kez İŞLEMİŞ olana dek bekler (Consumed, tüketici bitince dolar).
    private async Task PublishAndWaitAsync(OrderCancelled e, int times = 1)
    {
        for (var i = 0; i < times; i++) await Harness.Bus.Publish(e, Ct);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Harness.Consumed.Select<OrderCancelled>(m => m.Context.Message.EventId == e.EventId, Ct).Count() < times)
        {
            Assert.True(DateTime.UtcNow < deadline, "Tüketici zamanında işlemedi.");
            await Task.Delay(50, Ct);
        }
    }

    [Fact]
    public async Task OrderCancelled_ReleasesReservedStock()
    {
        var a = await CreateProductAsync(stock: 10);
        var b = await CreateProductAsync(stock: 5);
        var orderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(orderId, (a.Id, 3), (b.Id, 2))).StatusCode);
        Assert.Equal(7, await StockOfAsync(a.Id));
        Assert.Equal(3, await StockOfAsync(b.Id));

        await PublishAndWaitAsync(Cancelled(orderId));

        Assert.Equal(10, await StockOfAsync(a.Id));
        Assert.Equal(5, await StockOfAsync(b.Id));
    }

    [Fact]
    public async Task OrderCancelled_DeliveredTwice_ReleasesStockOnlyOnce()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 4));
        Assert.Equal(6, await StockOfAsync(product.Id));

        // Aynı olay iki kez (en-az-bir-kez teslim): ikinci iade stok eklememeli (10, 14 değil).
        await PublishAndWaitAsync(Cancelled(orderId), times: 2);

        Assert.Equal(10, await StockOfAsync(product.Id));
        Assert.False(await Harness.Published.Any<Fault<OrderCancelled>>(m => m.Context.Message.Message.OrderId == orderId, Ct));
    }

    [Fact]
    public async Task OrderCancelled_ForOrderWithoutReservation_IsHarmless()
    {
        var product = await CreateProductAsync(stock: 10);
        var e = Cancelled(Guid.NewGuid()); // rezervasyon yok (ör. stok yetmediği için iptal edilmiş sipariş)

        await PublishAndWaitAsync(e);

        Assert.Equal(10, await StockOfAsync(product.Id));
        Assert.False(await Harness.Published.Any<Fault<OrderCancelled>>(m => m.Context.Message.Message.EventId == e.EventId, Ct));
    }

    [Fact]
    public async Task OrderCancelled_TwoDistinctEventsForSameOrder_StillReleasesOnce()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 2));

        await PublishAndWaitAsync(Cancelled(orderId));
        await PublishAndWaitAsync(Cancelled(orderId)); // farklı EventId, aynı sipariş: yine tek iade

        Assert.Equal(10, await StockOfAsync(product.Id));
    }
}
