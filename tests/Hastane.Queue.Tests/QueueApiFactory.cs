using Hastane.Queue.Api.Messaging;
using Hastane.TestSupport;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Hastane.Queue.Tests;

// Tüm test sınıfları TEK konteyneri paylaşır (collection fixture); her test kendi doktor/hasta kimlikleriyle çalışır
// (günlük numara doktor başınadır, yeni doktor = numara 1'den başlar). Migration'lar Development açılışında uygulanır.
public class QueueApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        builder.UseTestJwt();
        builder.UseSetting("Services:TimeoutSeconds", "2");
        // Appointment servisi sahte: gerçek ağ yok (bkz. FakeDownstream).
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(FakeDownstream.CreateHandler));
            // Üretimdeki tüketici yapılandırması + MassTransit test harness (bellek içi taşıma; RabbitMQ yok).
            services.RemoveMassTransit();
            services.AddMassTransitTestHarness(x => QueueMessaging.Configure(x));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class QueueCollection : ICollectionFixture<QueueApiFactory>
{
    public const string Name = "queue-api";
}
