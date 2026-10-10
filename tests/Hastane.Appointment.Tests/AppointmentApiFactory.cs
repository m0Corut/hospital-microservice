using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Hastane.Appointment.Tests;

// Test sınıfı başına gerçek bir SQL Server konteyneri açar; bitince siler.
// Migration'lar uygulama açılışında (Development ortamı) çalışır.
public class AppointmentApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Doctor servisinin sahte hali (gerçek ağ yok). Kayıtsız doktor için geniş program: bkz. FakeDoctorService.
    public FakeDoctorService Doctor { get; } = new();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        // Zaman aşımı testi 1 sn'de biter (üretim varsayılanı 5 sn).
        builder.UseSetting("Services:TimeoutSeconds", "1");
        builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(Doctor.CreateHandler)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
