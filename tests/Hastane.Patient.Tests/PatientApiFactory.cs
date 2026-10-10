using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Hastane.Patient.Tests;

// Tüm test sınıfları TEK konteyneri paylaşır (collection fixture); her test kendi (benzersiz) kullanıcı kimlikleriyle çalışır.
// Migration'lar uygulama açılışında (Development ortamı) çalışır.
public class PatientApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        // Zaman aşımı testi 1 sn'de biter (üretim varsayılanı 5 sn).
        builder.UseSetting("Services:TimeoutSeconds", "1");
        // Appointment ve Doctor servisleri sahte: gerçek ağ yok (bkz. FakeDownstream).
        builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(FakeDownstream.CreateHandler)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PatientCollection : ICollectionFixture<PatientApiFactory>
{
    public const string Name = "patient-api";
}
