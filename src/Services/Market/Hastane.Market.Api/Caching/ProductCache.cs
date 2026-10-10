using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hastane.Market.Api.Settings;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hastane.Market.Api.Caching;

// Ürün önbelleği (cache-aside). KURAL: önbellek yalnız hızlandırıcıdır, doğruluk kaynağı SQL Server.
// Redis kapalıysa ya da hata verirse her işlem sessizce "önbellek yok" gibi davranır (istisna yukarı çıkmaz).
public interface IProductCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class;

    Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class;

    // Ürün yazıldı / stok değişti: ilgili ürün anahtarları ve TÜM liste önbelleği silinir (liste, ürünün stokunu ve adını da taşır).
    Task InvalidateAsync(IEnumerable<Guid> productIds, CancellationToken ct);
}

public sealed class ProductCache(IRedisGate redis, IOptions<MarketOptions> options, ILogger<ProductCache> logger) : IProductCache
{
    public const string ProductKeyPrefix = "product:";
    public const string ListKeyPrefix = "products:list:";

    // Hangi liste anahtarlarının var olduğunu tutan küme: KEYS/SCAN taramasına gerek kalmadan hepsini silebilmek için.
    public const string ListIndexKey = "products:list-index";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ProductKey(Guid id) => ProductKeyPrefix + id;

    // Sorgu parametreleri kullanıcı girdisi (arama metni): anahtara doğrudan koymak yerine özetlenir.
    public static string ListKey(string? search, string? category, int page, int pageSize)
    {
        var raw = $"{search?.Trim().ToLowerInvariant()}|{category?.Trim().ToLowerInvariant()}|{page}|{pageSize}";
        return ListKeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        var db = redis.Database;
        if (db is null) return null;
        try
        {
            var value = await db.StringGetAsync(key).WaitAsync(ct);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>((string)value!, Json);
        }
        catch (Exception ex) when (RedisFailures.IsConnectivity(ex) || ex is JsonException)
        {
            logger.LogWarning("Önbellek okunamadı ({ErrorType}): veritabanından devam ediliyor.", ex.GetType().Name);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class
    {
        var db = redis.Database;
        if (db is null) return;
        try
        {
            var ttl = TimeSpan.FromMinutes(options.Value.ProductCacheMinutes);
            await db.StringSetAsync(key, JsonSerializer.Serialize(value, Json), ttl).WaitAsync(ct);
            if (key.StartsWith(ListKeyPrefix, StringComparison.Ordinal))
            {
                await db.SetAddAsync(ListIndexKey, key).WaitAsync(ct);
                await db.KeyExpireAsync(ListIndexKey, ttl * 2).WaitAsync(ct);
            }
        }
        catch (Exception ex) when (RedisFailures.IsConnectivity(ex))
        {
            logger.LogWarning("Önbellek yazılamadı ({ErrorType}): önbelleksiz devam ediliyor.", ex.GetType().Name);
        }
    }

    public async Task InvalidateAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var db = redis.Database;
        if (db is null) return;
        try
        {
            var keys = new List<RedisKey>();
            keys.AddRange(productIds.Distinct().Select(id => (RedisKey)ProductKey(id)));
            var listKeys = await db.SetMembersAsync(ListIndexKey).WaitAsync(ct);
            keys.AddRange(listKeys.Select(k => (RedisKey)(string)k!));
            keys.Add(ListIndexKey);
            await db.KeyDeleteAsync(keys.ToArray()).WaitAsync(ct);
        }
        catch (Exception ex) when (RedisFailures.IsConnectivity(ex))
        {
            // Silinemeyen anahtar en geç TTL (5 dk) sonunda kendiliğinden düşer.
            logger.LogWarning("Önbellek temizlenemedi ({ErrorType}): kayıtlar süre dolunca düşer.", ex.GetType().Name);
        }
    }
}
