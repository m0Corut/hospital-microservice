using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MariaDb;

namespace Hastane.Order.Tests;

// Tüm test sınıfları TEK MariaDB konteynerini paylaşır (collection fixture); her test kendi hasta kimlikleriyle çalışır.
// Migration'lar uygulama açılışında (Development ortamı) çalışır. Market sahtedir (FakeMarket): gerçek ağ yok.
public class OrderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Üretimdeki docker-compose ile aynı sürüm.
    private readonly MariaDbContainer _db = new MariaDbBuilder("mariadb:11.4").Build();

    public FakeMarket Market { get; } = new();

    public async ValueTask InitializeAsync() => await _db.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        builder.UseSetting("Internal:ApiKey", FakeMarket.InternalKey);
        // Zaman aşımı testi 1 sn'de biter (üretim varsayılanı 5 sn).
        builder.UseSetting("Services:TimeoutSeconds", "1");
        builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => Market.CreateHandler())));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class OrderCollection : ICollectionFixture<OrderApiFactory>
{
    public const string Name = "order-api";
}
