using System.Net;
using System.Net.Http.Json;
using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Hastane.Market.Tests;

// Ürün önbelleği (cache-aside): ikinci okuma Redis'ten gelir, yazma/stok değişimi anahtarları siler.
[Collection(MarketCollection.Name)]
public class ProductCacheTests(MarketApiFactory factory) : MarketTestBase(factory)
{
    private IDatabase Redis => Factory.RedisDb;

    [Fact]
    public async Task SecondRead_ComesFromRedis_NotFromDatabase()
    {
        var product = await CreateProductAsync(name: Unique("Önbellek"));
        Assert.False(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));

        // 1. okuma: veritabanından, önbelleğe yazar.
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);
        Assert.True(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));

        // Veritabanını API'yi atlayarak değiştir: önbellek bunu bilmez.
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET Name = {"DB-DEGISTI"} WHERE Id = {product.Id}", Ct));

        // 2. okuma: hâlâ eski ad geliyorsa yanıt Redis'ten gelmiştir.
        var second = await ReadAsync<ResultProductDto>(await Anonymous.GetAsync($"/api/products/{product.Id}", Ct));
        Assert.Equal(product.Name, second.Name);
        // Yönetici okuması önbelleği atlar ve gerçeği görür.
        Assert.Equal("DB-DEGISTI", (await GetTruthAsync(product.Id)).Name);
    }

    [Fact]
    public async Task CachedProduct_HasFiveMinuteTtl()
    {
        var product = await CreateProductAsync();
        await Anonymous.GetAsync($"/api/products/{product.Id}", Ct);

        var ttl = await Redis.KeyTimeToLiveAsync(ProductCache.ProductKey(product.Id));

        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Update_DeletesProductKey_AndNextReadShowsNewData()
    {
        var product = await CreateProductAsync(name: Unique("Eski"));
        await Anonymous.GetAsync($"/api/products/{product.Id}", Ct);
        Assert.True(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));

        var newName = Unique("Yeni");
        var put = await Admin.PutAsJsonAsync($"/api/products/{product.Id}", ProductBody(name: newName, price: 77m), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        Assert.False(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));
        var fresh = await ReadAsync<ResultProductDto>(await Anonymous.GetAsync($"/api/products/{product.Id}", Ct));
        Assert.Equal(newName, fresh.Name);
        Assert.Equal(77m, fresh.Price);
    }

    [Fact]
    public async Task Delete_DeletesProductKey()
    {
        var product = await CreateProductAsync();
        await Anonymous.GetAsync($"/api/products/{product.Id}", Ct);
        Assert.True(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));

        await Admin.DeleteAsync($"/api/products/{product.Id}", Ct);

        Assert.False(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task ListCache_IsFilled_ThenClearedWhenAnyProductChanges()
    {
        var token = Unique("liste");
        await CreateProductAsync(name: $"{token} A");
        var url = $"/api/products?search={token}";

        var first = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync(url, Ct));
        Assert.Equal(1, first.TotalCount);
        var listKey = ProductCache.ListKey(token, null, 1, 12);
        Assert.True(await Redis.KeyExistsAsync(listKey));

        // Yeni ürün eklenince liste önbelleği silinir; ikinci okuma yeni ürünü görür.
        await CreateProductAsync(name: $"{token} B");
        Assert.False(await Redis.KeyExistsAsync(listKey));
        var second = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync(url, Ct));
        Assert.Equal(2, second.TotalCount);
    }

    [Fact]
    public async Task ListRead_ComesFromRedis_OnSecondCall()
    {
        var token = Unique("liste");
        var product = await CreateProductAsync(name: $"{token} A");
        var url = $"/api/products?search={token}";
        await Anonymous.GetAsync(url, Ct);

        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET Price = 1 WHERE Id = {product.Id}", Ct));

        var second = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync(url, Ct));
        Assert.Equal(100m, Assert.Single(second.Items).Price); // eski fiyat: önbellekten
    }

    [Fact]
    public async Task StockReservation_ClearsProductCache()
    {
        var product = await CreateProductAsync(stock: 10);
        var before = await ReadAsync<ResultProductDto>(await Anonymous.GetAsync($"/api/products/{product.Id}", Ct));
        Assert.Equal(10, before.Stock);
        Assert.True(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));

        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(Guid.NewGuid(), (product.Id, 4))).StatusCode);

        Assert.False(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));
        var after = await ReadAsync<ResultProductDto>(await Anonymous.GetAsync($"/api/products/{product.Id}", Ct));
        Assert.Equal(6, after.Stock);
    }

    [Fact]
    public async Task InactiveProduct_IsNeverCached_ForAnonymous()
    {
        var product = await CreateProductAsync(isActive: false);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);

        Assert.False(await Redis.KeyExistsAsync(ProductCache.ProductKey(product.Id)));
    }

    [Fact]
    public async Task CorruptCacheValue_FallsBackToDatabase()
    {
        var product = await CreateProductAsync(name: Unique("Bozuk"));
        await Redis.StringSetAsync(ProductCache.ProductKey(product.Id), "{bozuk json");

        var response = await Anonymous.GetAsync($"/api/products/{product.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(product.Name, (await ReadAsync<ResultProductDto>(response)).Name);
    }
}

// Redis KAPALI: ürün uçları önbelleksiz çalışır (hata yok), sepet 503 verir, iç stok uçları çalışır.
public class RedisDownTests(RedisDownMarketApiFactory factory) : MarketTestBase<RedisDownMarketApiFactory>(factory), IClassFixture<RedisDownMarketApiFactory>
{
    [Fact]
    public async Task ProductEndpoints_WorkWithoutRedis()
    {
        var token = Unique("redisyok");
        var created = await CreateProductAsync(name: $"{token} A", stock: 5);

        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync($"/api/products/{created.Id}", Ct)).StatusCode);
        var list = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync($"/api/products?search={token}", Ct));
        Assert.Equal(1, list.TotalCount);

        var put = await Admin.PutAsJsonAsync($"/api/products/{created.Id}", ProductBody(name: $"{token} B"), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal($"{token} B", (await ReadAsync<ResultProductDto>(await Anonymous.GetAsync($"/api/products/{created.Id}", Ct))).Name);

        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"/api/products/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task CartEndpoints_Return503_WithoutRedis_ButProductsStayUp()
    {
        var product = await CreateProductAsync();
        var patient = PatientClient(Unique("pat"));

        var get = await patient.GetAsync("/api/cart", Ct);
        var put = await patient.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 1 }, Json, Ct);
        var delete = await patient.DeleteAsync("/api/cart", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, put.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task InternalStock_WorksWithoutRedis()
    {
        var product = await CreateProductAsync(stock: 5);
        var orderId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK, (await ReserveAsync(orderId, (product.Id, 2))).StatusCode);
        Assert.Equal(3, await StockOfAsync(product.Id));
        Assert.Equal(HttpStatusCode.OK, (await ReleaseAsync(orderId)).StatusCode);
        Assert.Equal(5, await StockOfAsync(product.Id));
    }
}
