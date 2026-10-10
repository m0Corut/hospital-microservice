using Hastane.Market.Api.Settings;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hastane.Market.Api.Caching;

// Sepetin Redis'teki karşılığı: `cart:{userId}` HASH, alan = productId, değer = adet. HASH seçildi çünkü
// "şu ürünün adedini yaz" tek atomik komut (HSET); JSON bloğunda oku-değiştir-yaz yarışı olurdu.
// Fiyat ve ad BURADA TUTULMAZ: okurken ürün kaydından gelir.
public interface ICartStore
{
    Task<IReadOnlyDictionary<Guid, int>> GetAsync(string userId, CancellationToken ct);

    // quantity 0 = alanı sil. Her yazmada sepetin 7 günlük ömrü yeniden başlar.
    Task SetAsync(string userId, Guid productId, int quantity, CancellationToken ct);

    Task ClearAsync(string userId, CancellationToken ct);
}

public sealed class RedisCartStore(IRedisGate redis, IOptions<MarketOptions> options) : ICartStore
{
    public static string Key(string userId) => "cart:" + userId;

    private TimeSpan Ttl => TimeSpan.FromDays(options.Value.CartDays);

    public Task<IReadOnlyDictionary<Guid, int>> GetAsync(string userId, CancellationToken ct)
        => RunAsync(async db =>
        {
            var entries = await db.HashGetAllAsync(Key(userId)).WaitAsync(ct);
            var result = new Dictionary<Guid, int>();
            foreach (var entry in entries)
            {
                // Bozuk alan (elle yazılmış, eski sürüm) sepeti düşürmesin: atlanır.
                if (Guid.TryParse(entry.Name.ToString(), out var id) && int.TryParse(entry.Value.ToString(), out var qty) && qty > 0)
                    result[id] = qty;
            }
            return (IReadOnlyDictionary<Guid, int>)result;
        });

    public Task SetAsync(string userId, Guid productId, int quantity, CancellationToken ct)
        => RunAsync(async db =>
        {
            var key = Key(userId);
            var field = (RedisValue)productId.ToString();

            // İşlem (MULTI/EXEC): yazma ve süre yenileme birlikte uygulanır ya da hiç.
            var tran = db.CreateTransaction();
            var write = quantity > 0 ? tran.HashSetAsync(key, field, quantity) : tran.HashDeleteAsync(key, field);
            var expire = tran.KeyExpireAsync(key, Ttl);
            if (!await tran.ExecuteAsync().WaitAsync(ct)) throw new RedisUnavailableException("Sepet yazılamadı.");
            await Task.WhenAll(write, expire);
            return true;
        });

    public Task ClearAsync(string userId, CancellationToken ct)
        => RunAsync(async db => await db.KeyDeleteAsync(Key(userId)).WaitAsync(ct));

    // Redis'e ulaşılamıyorsa tek tip hata: servis bunu 503'e çevirir.
    private async Task<T> RunAsync<T>(Func<IDatabase, Task<T>> action)
    {
        var db = redis.Database ?? throw new RedisUnavailableException("Redis bağlı değil.");
        try
        {
            return await action(db);
        }
        catch (Exception ex) when (RedisFailures.IsConnectivity(ex))
        {
            throw new RedisUnavailableException("Redis'e ulaşılamadı.", ex);
        }
    }
}
