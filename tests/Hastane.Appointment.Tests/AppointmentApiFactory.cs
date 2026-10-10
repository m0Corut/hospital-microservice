using Hastane.Appointment.Api.Settings;
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
        // Notification de sahte (varsayılan: hep 201): mevcut testler gerçek ağa çıkmaz, bildirim varlığından etkilenmez.
        builder.UseSetting("Services:NotificationBaseUrl", $"http://{FakeNotificationService.Host}");
        builder.UseSetting("Services:NotificationTimeoutSeconds", "1");
        builder.ConfigureTestServices(services =>
        {
            // user-secrets'taki gerçek anahtar testi etkilemesin: seçenek son söz olarak sabit test değerine çekilir.
            services.PostConfigure<InternalOptions>(o => o.ApiKey = TestInternalKey);
            services.ConfigureHttpClientDefaults(b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new RoutingFakeHandler(Doctor, Notification)));
        });
    }

    public const string TestInternalKey = "test-internal-key-appointment";

    // Notification servisinin sahte hali; gönderilen bildirimler Calls'ta, hata kipleri Mode ile.
    public FakeNotificationService Notification { get; } = new();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
