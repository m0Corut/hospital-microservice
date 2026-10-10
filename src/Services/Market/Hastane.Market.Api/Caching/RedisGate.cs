using StackExchange.Redis;

namespace Hastane.Market.Api.Caching;

// Redis'e tek kapı. Bağlantı kurulamadıysa ya da koptuysa Database null döner: çağıran "Redis yok" yolunu izler
// (önbellek: atla; sepet: 503). AbortOnConnectFail=false olduğundan Redis KAPALIYKEN servis ayağa kalkar, Redis sonradan
// açılırsa çoklayıcı kendiliğinden yeniden bağlanır.
public interface IRedisGate
{
    // Bağlıysa veritabanı, değilse null.
    IDatabase? Database { get; }
}

// Redis çağrısı bağlantı/zaman aşımı yüzünden yapılamadı.
public sealed class RedisUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public static class RedisFailures
{
    // Yalnız "Redis'e ulaşılamadı" türü hatalar; mantık hatası (ör. yanlış tür) yutulmaz.
    public static bool IsConnectivity(Exception ex) => ex switch
    {
        RedisConnectionException or RedisTimeoutException or TimeoutException or ObjectDisposedException => true,
        // Parola yanlışı (NOAUTH/WRONGPASS) ve yükleme/salt-okunur durumları da "kullanılamıyor" sayılır.
        RedisServerException s => s.Message.StartsWith("NOAUTH", StringComparison.OrdinalIgnoreCase)
                                  || s.Message.StartsWith("WRONGPASS", StringComparison.OrdinalIgnoreCase)
                                  || s.Message.StartsWith("LOADING", StringComparison.OrdinalIgnoreCase)
                                  || s.Message.StartsWith("READONLY", StringComparison.OrdinalIgnoreCase)
                                  || s.Message.StartsWith("MISCONF", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}

public sealed class RedisGate : IRedisGate, IDisposable
{
    private readonly Lazy<IConnectionMultiplexer?> _multiplexer;

    public RedisGate(IConfiguration configuration, ILogger<RedisGate> logger)
    {
        // Tembel: bağlantı cümlesi ilk kullanımda okunur (testte WebApplicationFactory'nin verdiği adres geçerli olsun).
        _multiplexer = new Lazy<IConnectionMultiplexer?>(() => Connect(configuration.GetConnectionString("Redis"), logger));
    }

    public IDatabase? Database
    {
        get
        {
            var mux = _multiplexer.Value;
            return mux is { IsConnected: true } ? mux.GetDatabase() : null;
        }
    }

    private static IConnectionMultiplexer? Connect(string? connectionString, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("ConnectionStrings:Redis boş: ürün önbelleği kapalı, sepet uçları 503 verir.");
            return null;
        }

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            // Bağlantı yokken komutlar kuyrukta beklemesin, hemen hata versin (aksi halde her istek zaman aşımı kadar bekler).
            options.BacklogPolicy = BacklogPolicy.FailFast;
            options.ConnectRetry = 1;
            if (!connectionString.Contains("connectTimeout", StringComparison.OrdinalIgnoreCase)) options.ConnectTimeout = 2000;
            if (!connectionString.Contains("syncTimeout", StringComparison.OrdinalIgnoreCase)) options.SyncTimeout = 2000;
            if (!connectionString.Contains("asyncTimeout", StringComparison.OrdinalIgnoreCase)) options.AsyncTimeout = 2000;
            return ConnectionMultiplexer.Connect(options);
        }
        catch (Exception ex)
        {
            // Bağlantı cümlesindeki parola günlüğe girmesin: yalnız istisna türü yazılır.
            logger.LogWarning("Redis bağlantısı kurulamadı ({ErrorType}): önbellek kapalı, sepet uçları 503 verir.", ex.GetType().Name);
            return null;
        }
    }

    public void Dispose()
    {
        if (_multiplexer.IsValueCreated) _multiplexer.Value?.Dispose();
    }
}
