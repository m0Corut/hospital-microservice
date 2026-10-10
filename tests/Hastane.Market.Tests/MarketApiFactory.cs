using Hastane.Market.Api.Settings;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Hastane.Market.Tests;

// Gerçek SQL Server + gerçek Redis konteyneri. Tüm test sınıfları TEK örneği paylaşır (collection fixture);
// her test kendi benzersiz ürün/kullanıcı kimlikleriyle çalışır. Migration'lar açılışta (Development ortamı) uygulanır.
public class MarketApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestInternalKey = "test-internal-key-market";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4").Build();
    private IConnectionMultiplexer? _redisConnection;

    // Alt sınıflar "Redis kapalı" ya da "iç anahtar ayarı yok" durumlarını kurar.
    protected virtual bool UseRedis => true;

    protected virtual string InternalKey => TestInternalKey;

    // Testin Redis'e doğrudan bakıp (anahtar var mı, TTL kaç?) servisin gördüğünden bağımsız doğrulama yapması için.
    public IDatabase RedisDb => (_redisConnection ??= ConnectionMultiplexer.Connect(_redis.GetConnectionString())).GetDatabase();

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();
        if (UseRedis) await _redis.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        // 127.0.0.1:1 -> kimsenin dinlemediği port: bağlantı anında reddedilir ("Redis kapalı" senaryosu).
        builder.UseSetting("ConnectionStrings:Redis", UseRedis ? _redis.GetConnectionString() : "127.0.0.1:1");
        builder.UseSetting("Market:SeedDemoData", "false");
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        builder.ConfigureServices(services =>
            // user-secrets'taki gerçek anahtar testi etkilemesin: seçenek son söz olarak sabit değere çekilir.
            services.PostConfigure<InternalOptions>(o => o.ApiKey = InternalKey));
    }

    public override async ValueTask DisposeAsync()
    {
        _redisConnection?.Dispose();
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

// Redis'e ulaşılamıyor: ürün uçları çalışmalı, sepet 503 vermeli.
public class RedisDownMarketApiFactory : MarketApiFactory
{
    protected override bool UseRedis => false;
}

// Internal:ApiKey ayarı BOŞ: iç uçlar her koşulda 401 vermeli.
public class NoKeyMarketApiFactory : MarketApiFactory
{
    protected override bool UseRedis => false;

    protected override string InternalKey => string.Empty;
}

[CollectionDefinition(Name)]
public class MarketCollection : ICollectionFixture<MarketApiFactory>
{
    public const string Name = "market-api";
}
