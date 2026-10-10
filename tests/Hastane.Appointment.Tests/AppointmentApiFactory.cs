using Hastane.Appointment.Api.Messaging;
using Hastane.Contracts;
using Hastane.TestSupport;
using MassTransit;
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

    protected string ConnectionString => _sql.GetConnectionString();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        // Zaman aşımı testi 1 sn'de biter (üretim varsayılanı 5 sn).
        builder.UseSetting("Services:TimeoutSeconds", "1");
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => Doctor.CreateHandler()));
            ConfigureMessaging(services);
        });
    }

    // Varsayılan: üretimdeki outbox yapılandırması + MassTransit TEST HARNESS (bellek içi taşıma). RabbitMQ yok.
    // Olay yine EF outbox üzerinden gider: randevu SaveChanges -> OutboxMessage -> teslim servisi -> taşıma -> test dinleyicisi.
    // Outbox teslimi Send olduğundan olay harness.Published'da DEĞİL, harness.Consumed'da görünür (bkz. OutboxTapConsumer).
    protected virtual void ConfigureMessaging(IServiceCollection services)
    {
        services.RemoveMassTransit();
        services.AddMassTransitTestHarness(x =>
        {
            AppointmentMessaging.Configure(x, o => o.QueryDelay = OutboxTapExtensions.TestQueryDelay);
            x.AddOutboxTap<AppointmentCreated>();
            x.AddOutboxTap<AppointmentStatusChanged>();
            x.AddOutboxTap<AppointmentRescheduled>();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

// RabbitMQ'ya ULAŞILAMAYAN servis: gerçek RabbitMQ taşıması, kapalı bir porta bakar (127.0.0.1:1). Outbox sayesinde randevu işlemleri
// yine başarılı olmalı; olaylar OutboxMessage tablosunda teslim edilmeyi beklemeli.
public class RabbitDownAppointmentApiFactory : AppointmentApiFactory
{
    protected override void ConfigureMessaging(IServiceCollection services)
    {
        services.RemoveMassTransit();
        services.AddMassTransit(x =>
        {
            AppointmentMessaging.Configure(x);
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host("127.0.0.1", 1, "/", h =>
                {
                    h.Username("guest");
                    h.Password("guest");
                });
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}
