using System.Net;
using System.Net.Http.Json;
using Hastane.Market.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Tests;

// İç stok uçları: reserve (atomik, hepsi-ya-hiç, idempotent) ve release (idempotent, yalnız rezerve edilen miktar).
[Collection(MarketCollection.Name)]
public class InternalStockTests(MarketApiFactory factory) : MarketTestBase(factory)
{
    // --- Anahtar ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yanlis-anahtar")]
    [InlineData("test-internal-key-marke")] // doğrusunun öneki
    public async Task Reserve_And_Release_WithMissingOrWrongKey_Return401_AndChangeNothing(string? key)
    {
        var product = await CreateProductAsync(stock: 5);
        var orderId = Guid.NewGuid();

        var reserve = await Anonymous.SendAsync(InternalRequest("/api/internal/stock/reserve", ReserveBody(orderId, (product.Id, 1)), key), Ct);
        var release = await Anonymous.SendAsync(InternalRequest("/api/internal/stock/release", new { orderId }, key), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, reserve.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, release.StatusCode);
        Assert.Equal(5, await StockOfAsync(product.Id));
        Assert.Empty(await WithDbAsync(db => db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync(Ct)));
    }

    [Fact]
    public async Task InternalEndpoints_IgnoreJwt_JwtDoesNotReplaceTheKey()
    {
        var product = await CreateProductAsync(stock: 5);
        var request = InternalRequest("/api/internal/stock/reserve", ReserveBody(Guid.NewGuid(), (product.Id, 1)), key: null);

        // Admin token'ı iç anahtarın yerini tutmaz.
        var response = await Admin.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Reserve ---

    [Fact]
    public async Task Reserve_DecrementsStock_AndStoresReservation()
    {
        var a = await CreateProductAsync(stock: 10);
        var b = await CreateProductAsync(stock: 4);
        var orderId = Guid.NewGuid();

        var response = await ReserveAsync(orderId, (a.Id, 3), (b.Id, 4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync<ReservationResultDto>(response);
        Assert.Equal("Reserved", body.Status);
        Assert.False(body.Replayed);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(7, await StockOfAsync(a.Id));
        Assert.Equal(0, await StockOfAsync(b.Id)); // tam stok kadarı satılabilir
        var stored = await WithDbAsync(db => db.StockReservations.Include(r => r.Items).SingleAsync(r => r.OrderId == orderId, Ct));
        Assert.Equal(2, stored.Items.Count);
    }

    [Fact]
    public async Task Reserve_SameOrderTwice_IsIdempotent_StockDecrementedOnce()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();

        var first = await ReserveAsync(orderId, (product.Id, 4));
        var second = await ReserveAsync(orderId, (product.Id, 4));
        // Tekrar farklı gövdeyle gelse bile ilk kayıt geçerli: sipariş kalemleri sonradan değiştirilemez.
        var third = await ReserveAsync(orderId, (product.Id, 9));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.False((await ReadAsync<ReservationResultDto>(first)).Replayed);
        Assert.True((await ReadAsync<ReservationResultDto>(second)).Replayed);
        Assert.Equal(4, Assert.Single((await ReadAsync<ReservationResultDto>(third)).Items).Quantity);
        Assert.Equal(6, await StockOfAsync(product.Id));
        Assert.Single(await WithDbAsync(db => db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync(Ct)));
    }

    [Fact]
    public async Task Reserve_AllOrNothing_WhenOneItemIsShort()
    {
        var plenty = await CreateProductAsync(stock: 5);
        var scarce = await CreateProductAsync(stock: 1);
        var orderId = Guid.NewGuid();

        var response = await ReserveAsync(orderId, (plenty.Id, 2), (scarce.Id, 2));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await JsonBodyAsync(response);
        Assert.Equal(scarce.Id, json.GetProperty("productId").GetGuid());
        Assert.Equal("InsufficientStock", json.GetProperty("reason").GetString());
        Assert.Equal(2, json.GetProperty("requested").GetInt32());
        Assert.Equal(1, json.GetProperty("available").GetInt32());
        // Hiçbiri düşmedi, rezervasyon kaydı da yok.
        Assert.Equal(5, await StockOfAsync(plenty.Id));
        Assert.Equal(1, await StockOfAsync(scarce.Id));
        Assert.Empty(await WithDbAsync(db => db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync(Ct)));

        // Reddedilen sipariş numarası "zehirlenmez": stok yetişince aynı orderId ile tekrar denenebilir.
        var retry = await ReserveAsync(orderId, (plenty.Id, 2), (scarce.Id, 1));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(3, await StockOfAsync(plenty.Id));
        Assert.Equal(0, await StockOfAsync(scarce.Id));
    }

    [Fact]
    public async Task Reserve_FailureOnFirstItemOrder_StillRollsBackEarlierItems()
    {
        // Kalem sırası ürün kimliğine göre sıralandığı için hangisinin "önce" düşeceği bilinmez; iki yönü de dene.
        var a = await CreateProductAsync(stock: 5);
        var b = await CreateProductAsync(stock: 5);
        var scarce = await CreateProductAsync(stock: 0);

        var response = await ReserveAsync(Guid.NewGuid(), (a.Id, 1), (scarce.Id, 1), (b.Id, 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(5, await StockOfAsync(a.Id));
        Assert.Equal(5, await StockOfAsync(b.Id));
    }

    [Fact]
    public async Task Reserve_InactiveOrUnknownProduct_Returns409WithReason_AndChangesNothing()
    {
        var active = await CreateProductAsync(stock: 5);
        var inactive = await CreateProductAsync(stock: 5, isActive: false);

        var inactiveResponse = await ReserveAsync(Guid.NewGuid(), (active.Id, 1), (inactive.Id, 1));
        var unknownId = Guid.NewGuid();
        var unknownResponse = await ReserveAsync(Guid.NewGuid(), (active.Id, 1), (unknownId, 1));

        Assert.Equal(HttpStatusCode.Conflict, inactiveResponse.StatusCode);
        var inactiveJson = await JsonBodyAsync(inactiveResponse);
        Assert.Equal(inactive.Id, inactiveJson.GetProperty("productId").GetGuid());
        Assert.Equal("ProductUnavailable", inactiveJson.GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.Conflict, unknownResponse.StatusCode);
        var unknownJson = await JsonBodyAsync(unknownResponse);
        Assert.Equal(unknownId, unknownJson.GetProperty("productId").GetGuid());
        Assert.Equal("ProductNotFound", unknownJson.GetProperty("reason").GetString());

        Assert.Equal(5, await StockOfAsync(active.Id));
        Assert.Equal(5, await StockOfAsync(inactive.Id));
    }

    [Fact]
    public async Task Reserve_DuplicateLines_AreMerged()
    {
        var product = await CreateProductAsync(stock: 10);

        var response = await ReserveAsync(Guid.NewGuid(), (product.Id, 2), (product.Id, 3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single((await ReadAsync<ReservationResultDto>(response)).Items);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(5, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Reserve_DuplicateLinesExceedingStock_Return409()
    {
        var product = await CreateProductAsync(stock: 4);

        var response = await ReserveAsync(Guid.NewGuid(), (product.Id, 2), (product.Id, 3));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(4, await StockOfAsync(product.Id));
    }

    public static TheoryData<string> InvalidReserveBodies() => new()
    {
        """{"orderId":"00000000-0000-0000-0000-000000000000","items":[{"productId":"11111111-1111-1111-1111-111111111111","quantity":1}]}""",
        """{"orderId":"22222222-2222-2222-2222-222222222222","items":[]}""",
        """{"orderId":"22222222-2222-2222-2222-222222222222"}""",
        """{"orderId":"22222222-2222-2222-2222-222222222222","items":[{"productId":"11111111-1111-1111-1111-111111111111","quantity":0}]}""",
        """{"orderId":"22222222-2222-2222-2222-222222222222","items":[{"productId":"11111111-1111-1111-1111-111111111111","quantity":-2}]}""",
        """{"orderId":"22222222-2222-2222-2222-222222222222","items":[{"productId":"00000000-0000-0000-0000-000000000000","quantity":1}]}""",
        """{"orderId":"yanlis","items":[]}""",
    };

    [Theory]
    [MemberData(nameof(InvalidReserveBodies))]
    public async Task Reserve_InvalidBody_Returns400(string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/stock/reserve")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Internal-Key", MarketApiFactory.TestInternalKey);

        var response = await Anonymous.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Eşzamanlılık ---

    [Fact]
    public async Task TenConcurrentReserves_NeverOversell_StockNeverGoesNegative()
    {
        var product = await CreateProductAsync(stock: 3);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => ReserveAsync(Guid.NewGuid(), (product.Id, 1)), Ct)));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockOfAsync(product.Id));
        Assert.Equal(3, await WithDbAsync(db => db.StockReservations.CountAsync(r => r.Items.Any(i => i.ProductId == product.Id), Ct)));
    }

    [Fact]
    public async Task TenConcurrentReserves_OfTwo_WithStockFive_ExactlyTwoSucceed()
    {
        var product = await CreateProductAsync(stock: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => ReserveAsync(Guid.NewGuid(), (product.Id, 2)), Ct)));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(8, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task TenConcurrentReserves_SameOrder_DecrementOnce_AllGet200()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => ReserveAsync(orderId, (product.Id, 1)), Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(ReadAsync<ReservationResultDto>));
        Assert.Equal(1, bodies.Count(b => !b.Replayed));
        Assert.Equal(9, await StockOfAsync(product.Id));
        Assert.Single(await WithDbAsync(db => db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync(Ct)));
    }

    [Fact]
    public async Task ConcurrentMultiItemReserves_InOppositeOrder_DoNotDeadlock()
    {
        var a = await CreateProductAsync(stock: 100);
        var b = await CreateProductAsync(stock: 100);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => i % 2 == 0
            ? ReserveAsync(Guid.NewGuid(), (a.Id, 1), (b.Id, 1))
            : ReserveAsync(Guid.NewGuid(), (b.Id, 1), (a.Id, 1)), Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(80, await StockOfAsync(a.Id));
        Assert.Equal(80, await StockOfAsync(b.Id));
    }

    // --- Release ---

    [Fact]
    public async Task Release_RestoresReservedStock_OnlyOnce()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 4));
        Assert.Equal(6, await StockOfAsync(product.Id));

        var first = await ReleaseAsync(orderId);
        var second = await ReleaseAsync(orderId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await ReadAsync<ReservationResultDto>(first);
        Assert.Equal("Released", firstBody.Status);
        Assert.False(firstBody.Replayed);
        Assert.True((await ReadAsync<ReservationResultDto>(second)).Replayed);
        Assert.Equal(10, await StockOfAsync(product.Id)); // iki kez iade edilseydi 14 olurdu
    }

    [Fact]
    public async Task Release_ReturnsOnlyWhatWasReserved_EvenIfAdminChangedStockMeanwhile()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 2));
        // Yönetici sayımdan sonra stoğu 50 yaptı.
        await Admin.PutAsJsonAsync($"/api/products/{product.Id}", ProductBody(name: product.Name, stock: 50), Json, Ct);

        await ReleaseAsync(orderId);

        Assert.Equal(52, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Release_UnknownOrder_Returns200_AndChangesNothing()
    {
        var product = await CreateProductAsync(stock: 10);

        var response = await ReleaseAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("None", (await ReadAsync<ReservationResultDto>(response)).Status);
        Assert.Equal(10, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Release_EmptyOrderId_Returns400()
    {
        using var request = InternalRequest("/api/internal/stock/release", new { orderId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task TenConcurrentReleases_RestoreExactlyOnce()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 4));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => ReleaseAsync(orderId), Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(10, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Reserve_AfterRelease_DoesNotReserveAgain()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 4));
        await ReleaseAsync(orderId);

        var again = await ReserveAsync(orderId, (product.Id, 4));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var body = await ReadAsync<ReservationResultDto>(again);
        Assert.Equal("Released", body.Status);
        Assert.True(body.Replayed);
        Assert.Equal(10, await StockOfAsync(product.Id)); // iptal edilmiş siparişin stoğu yeniden düşmez
    }

    [Fact]
    public async Task Release_AfterProductDeleted_StillSucceeds()
    {
        var product = await CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (product.Id, 2));
        await Admin.DeleteAsync($"/api/products/{product.Id}", Ct);

        var response = await ReleaseAsync(orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Released", (await ReadAsync<ReservationResultDto>(response)).Status);
    }
}

// Internal:ApiKey ayarı BOŞ iken iç uçlar HER ZAMAN 401: "anahtar tanımlı değil = herkese açık" olmaz.
public class InternalKeyNotConfiguredTests(NoKeyMarketApiFactory factory) : MarketTestBase<NoKeyMarketApiFactory>(factory), IClassFixture<NoKeyMarketApiFactory>
{
    [Theory]
    [InlineData("/api/internal/stock/reserve", null)]
    [InlineData("/api/internal/stock/reserve", "")]
    [InlineData("/api/internal/stock/reserve", " ")]
    [InlineData("/api/internal/stock/reserve", "herhangi-bir-deger")]
    [InlineData("/api/internal/stock/release", null)]
    [InlineData("/api/internal/stock/release", "")]
    [InlineData("/api/internal/stock/release", "herhangi-bir-deger")]
    public async Task WhenKeySettingIsEmpty_InternalEndpoints_Always401(string url, string? key)
    {
        var product = await CreateProductAsync(stock: 5);
        object body = url.EndsWith("/reserve", StringComparison.Ordinal)
            ? ReserveBody(Guid.NewGuid(), (product.Id, 1))
            : new { orderId = Guid.NewGuid() };
        using var request = InternalRequest(url, body, key);

        var response = await Anonymous.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(5, await StockOfAsync(product.Id));
    }
}
