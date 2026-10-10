using Hastane.Appointment.Api.Data;
using MassTransit;

namespace Hastane.Appointment.Api.Messaging;

// ÖĞRETİM: Appointment olayları RabbitMQ'ya yayınlar (AppointmentCreated, AppointmentStatusChanged, AppointmentRescheduled). Yayın doğrudan değil, "outbox" üzerinden:
// olay, randevuyla AYNI veritabanı işleminde OutboxMessage tablosuna yazılır; arka plandaki teslim servisi onu RabbitMQ'ya taşır.
// Böylece RabbitMQ kapalıyken de randevu kaydedilir, olay kaybolmaz (en az bir kez teslim).
public static class AppointmentMessaging
{
    // Servis öneki: aynı RabbitMQ'yu paylaşan servislerin kuyruk adları çakışmasın (appointment-..., notification-... ).
    public const string EndpointPrefix = "appointment";

    // Üretimde de testte de AYNI olan kısım (outbox + adlandırma). Taşıma (RabbitMQ / bellek) ayrıca eklenir:
    // test kendi taşımasını (MassTransit test harness) verir.
    public static void Configure(IBusRegistrationConfigurator x)
    {
        x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(EndpointPrefix, includeNamespace: false));
        x.AddEntityFrameworkOutbox<AppointmentDbContext>(o =>
        {
            // SQL Server: MassTransit'in kilit SQL'i (UPDLOCK, ROWLOCK) kullanılır.
            o.UseSqlServer();
            // "Bus outbox": IPublishEndpoint.Publish çağrısı mesajı DbContext'e ekler; SaveChanges randevuyla birlikte yazar.
            o.UseBusOutbox();
            o.DisableInboxCleanupService();
        });
    }

    public static IServiceCollection AddAppointmentMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddMassTransit(x =>
        {
            Configure(x);
            var host = configuration["RabbitMq:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                // RabbitMq:Host boşsa (testler, RabbitMQ'suz geliştirme) bellek içi taşıma: aynı süreç içinde teslim.
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                return;
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, ushort.TryParse(configuration["RabbitMq:Port"], out var port) ? port : (ushort)5672,
                    configuration["RabbitMq:VirtualHost"] ?? "/", h =>
                    {
                        h.Username(configuration["RabbitMq:Username"] ?? "guest");
                        h.Password(configuration["RabbitMq:Password"] ?? "guest");
                    });
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}
