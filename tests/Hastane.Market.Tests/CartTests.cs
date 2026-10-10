using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Dtos;
using StackExchange.Redis;

namespace Hastane.Market.Tests;

// Sepet: Redis'te `cart:{userId}`; yalnız productId + adet saklanır, fiyat/ad her okumada ürün kaydından gelir.
[Collection(MarketCollection.Name)]
public class CartTests(MarketApiFactory factory) : MarketTestBase(factory)
{
    private IDatabase Redis => Factory.RedisDb;

    private static Task<HttpResponseMessage> PutItem(HttpClient client, Guid productId, int quantity)
        => client.PutAsJsonAsync($"/api/cart/items/{productId}", new { quantity }, Json, Ct);

    private static async Task<CartResultDto> GetCart(HttpClient client)
    {
        var response = await client.GetAsync("/api/cart", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<CartResultDto>(response);
    }

    [Fact]
    public async Task NewUser_HasEmptyCart_WithContractShape()
    {
        var response = await PatientClient(Unique("pat")).GetAsync("/api/cart", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonBodyAsync(response);
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, json.GetProperty("total").GetDecimal());
        Assert.Equal("TRY", json.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Put_AddsItem_AndReturnsCartWithServerSidePrice()
    {
        var product = await CreateProductAsync(name: Unique("Sepet"), price: 49.90m, stock: 10);
        var patient = PatientClient(Unique("pat"));

        var response = await PutItem(patient, product.Id, 3);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonBodyAsync(response);
        var item = Assert.Single(json.GetProperty("items").EnumerateArray().ToList());
        foreach (var field in new[] { "productId", "name", "unitPrice", "quantity", "lineTotal" })
            Assert.True(item.TryGetProperty(field, out _), $"'{field}' alanı yok.");
        Assert.Equal(product.Id, item.GetProperty("productId").GetGuid());
        Assert.Equal(product.Name, item.GetProperty("name").GetString());
        Assert.Equal(49.90m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(3, item.GetProperty("quantity").GetInt32());
        Assert.Equal(149.70m, item.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(149.70m, json.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Put_SetsAbsoluteQuantity_NotIncrement()
    {
        var product = await CreateProductAsync(stock: 10);
        var patient = PatientClient(Unique("pat"));

        await PutItem(patient, product.Id, 2);
        await PutItem(patient, product.Id, 5);

        Assert.Equal(5, Assert.Single((await GetCart(patient)).Items).Quantity);
    }

    [Fact]
    public async Task Put_Zero_RemovesItem()
    {
        var a = await CreateProductAsync(stock: 10);
        var b = await CreateProductAsync(stock: 10);
        var patient = PatientClient(Unique("pat"));
        await PutItem(patient, a.Id, 1);
        await PutItem(patient, b.Id, 2);

        var response = await PutItem(patient, a.Id, 0);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(b.Id, Assert.Single((await GetCart(patient)).Items).ProductId);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(100)]
    [InlineData(-1)]
    public async Task Put_QuantityOutOfRange_Returns400(int quantity)
    {
        var product = await CreateProductAsync(stock: 500);
        var patient = PatientClient(Unique("pat"));

        Assert.Equal(HttpStatusCode.BadRequest, (await PutItem(patient, product.Id, quantity)).StatusCode);
        Assert.Empty((await GetCart(patient)).Items);
    }

    [Fact]
    public async Task Put_Twenty_IsAllowed_WhenStockCovers()
    {
        var product = await CreateProductAsync(stock: 20);
        Assert.Equal(HttpStatusCode.OK, (await PutItem(PatientClient(Unique("pat")), product.Id, 20)).StatusCode);
    }

    [Fact]
    public async Task Put_AboveStock_Returns409_AndCartIsUnchanged()
    {
        var product = await CreateProductAsync(stock: 3);
        var patient = PatientClient(Unique("pat"));
        await PutItem(patient, product.Id, 2);

        var response = await PutItem(patient, product.Id, 4);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, Assert.Single((await GetCart(patient)).Items).Quantity);
        Assert.Equal(HttpStatusCode.OK, (await PutItem(patient, product.Id, 3)).StatusCode); // tam stok kadarı serbest
    }

    [Fact]
    public async Task Put_UnknownOrInactiveProduct_Returns404()
    {
        var inactive = await CreateProductAsync(isActive: false);
        var patient = PatientClient(Unique("pat"));

        Assert.Equal(HttpStatusCode.NotFound, (await PutItem(patient, Guid.NewGuid(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutItem(patient, inactive.Id, 1)).StatusCode);
        Assert.Empty((await GetCart(patient)).Items);
    }

    [Fact]
    public async Task Cart_ShowsCurrentPrice_WhenProductPriceChanges()
    {
        var product = await CreateProductAsync(price: 100m, stock: 10);
        var patient = PatientClient(Unique("pat"));
        await PutItem(patient, product.Id, 2);
        Assert.Equal(200m, (await GetCart(patient)).Total);

        await Admin.PutAsJsonAsync($"/api/products/{product.Id}", ProductBody(name: product.Name, price: 150m, stock: 10), Json, Ct);

        var cart = await GetCart(patient);
        Assert.Equal(150m, Assert.Single(cart.Items).UnitPrice);
        Assert.Equal(300m, cart.Total);
    }

    [Fact]
    public async Task Redis_StoresOnlyProductIdAndQuantity_NoPrice()
    {
        var sub = Unique("pat");
        var product = await CreateProductAsync(price: 123.45m, stock: 10);
        await PutItem(PatientClient(sub), product.Id, 4);

        var entries = await Redis.HashGetAllAsync(RedisCartStore.Key(sub));

        var entry = Assert.Single(entries);
        Assert.Equal(product.Id.ToString(), entry.Name.ToString());
        Assert.Equal("4", entry.Value.ToString()); // fiyat/ad yok, yalnız adet
    }

    [Fact]
    public async Task Cart_HasSevenDayTtl_AndEveryWriteRenewsIt()
    {
        var sub = Unique("pat");
        var product = await CreateProductAsync(stock: 10);
        var patient = PatientClient(sub);
        await PutItem(patient, product.Id, 1);

        var first = await Redis.KeyTimeToLiveAsync(RedisCartStore.Key(sub));
        Assert.NotNull(first);
        Assert.InRange(first!.Value, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7));

        // Süreyi elle 1 saate indir; yeni yazma 7 güne geri çıkarmalı.
        await Redis.KeyExpireAsync(RedisCartStore.Key(sub), TimeSpan.FromHours(1));
        await PutItem(patient, product.Id, 2);

        var renewed = await Redis.KeyTimeToLiveAsync(RedisCartStore.Key(sub));
        Assert.InRange(renewed!.Value, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Carts_AreIsolatedPerUser()
    {
        var product = await CreateProductAsync(stock: 10);
        var alice = PatientClient(Unique("alice"));
        var bob = PatientClient(Unique("bob"));

        await PutItem(alice, product.Id, 3);

        Assert.Equal(3, Assert.Single((await GetCart(alice)).Items).Quantity);
        Assert.Empty((await GetCart(bob)).Items);
    }

    [Fact]
    public async Task Delete_ClearsCart_AndRemovesRedisKey()
    {
        var sub = Unique("pat");
        var product = await CreateProductAsync(stock: 10);
        var patient = PatientClient(sub);
        await PutItem(patient, product.Id, 1);

        var response = await patient.DeleteAsync("/api/cart", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await Redis.KeyExistsAsync(RedisCartStore.Key(sub)));
        Assert.Empty((await GetCart(patient)).Items);
        Assert.Equal(HttpStatusCode.NoContent, (await patient.DeleteAsync("/api/cart", Ct)).StatusCode); // boş sepeti boşaltmak da sorun değil
    }

    [Fact]
    public async Task DeactivatedProduct_StaysInCart_MarkedUnavailable_AndSkipsTotal()
    {
        var keep = await CreateProductAsync(price: 10m, stock: 10);
        var gone = await CreateProductAsync(name: Unique("Pasifleşen"), price: 500m, stock: 10);
        var patient = PatientClient(Unique("pat"));
        await PutItem(patient, keep.Id, 2);
        await PutItem(patient, gone.Id, 1);

        await Admin.PutAsJsonAsync($"/api/products/{gone.Id}", ProductBody(name: gone.Name, price: 500m, stock: 10, isActive: false), Json, Ct);

        var cart = await GetCart(patient);
        Assert.Equal(2, cart.Items.Count);
        var unavailable = cart.Items.Single(i => i.ProductId == gone.Id);
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("Artık satışta değil", unavailable.Note);
        Assert.Equal(0m, unavailable.LineTotal);
        Assert.Equal(20m, cart.Total); // yalnız satıştaki ürün toplanır
        Assert.True(cart.Items.Single(i => i.ProductId == keep.Id).IsAvailable);
    }

    [Fact]
    public async Task DeletedProduct_IsMarkedUnavailable_AndCanBeRemoved()
    {
        var product = await CreateProductAsync(stock: 10);
        var patient = PatientClient(Unique("pat"));
        await PutItem(patient, product.Id, 1);
        await Admin.DeleteAsync($"/api/products/{product.Id}", Ct);

        var item = Assert.Single((await GetCart(patient)).Items);
        Assert.False(item.IsAvailable);
        Assert.Equal("Silinmiş ürün", item.Name);
        Assert.Equal(0m, (await GetCart(patient)).Total);

        Assert.Equal(HttpStatusCode.OK, (await PutItem(patient, product.Id, 0)).StatusCode); // silinmiş ürün de sepetten çıkarılabilir
        Assert.Empty((await GetCart(patient)).Items);
    }

    // --- Yetki ---

    [Fact]
    public async Task Cart_WithoutToken_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/cart", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutItem(Anonymous, Guid.NewGuid(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.DeleteAsync("/api/cart", Ct)).StatusCode);
    }

    [Fact]
    public async Task Cart_AsAdminOrDoctor_Returns403()
    {
        foreach (var client in new[] { Admin, ClientFor(DoctorToken()) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cart", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutItem(client, Guid.NewGuid(), 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/cart", Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Put_BodyWithoutQuantity_TreatedAsZero_NotCrash()
    {
        var patient = PatientClient(Unique("pat"));
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await patient.PutAsync($"/api/cart/items/{Guid.NewGuid()}", content, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // adet 0 = sil; olmayan ürünü silmek zararsız
        Assert.Equal(JsonValueKind.Array, (await JsonBodyAsync(response)).GetProperty("items").ValueKind);
    }
}
