using MassTransit;

namespace Hastane.Notification.Api.Messaging;

// ÖĞRETİM: Tüketici tarafı. Her tüketici için RabbitMQ'da ayrı bir kuyruk açılır (notification-appointment-created, ...).
// Hata olursa 3 kez artan aralıkla (1 sn, 5 sn, 15 sn) yeniden denenir; hâlâ olmuyorsa mesaj "<kuyruk>_error" kuyruğuna düşer
// (RabbitMQ yönetim arayüzü: http://localhost:15672 > Queues; mesajı oradan inceleyip Shovel/"Move messages" ile geri taşıyabilirsiniz).
public static class NotificationMessaging
{
    public const string EndpointPrefix = "notification";

    public static readonly TimeSpan[] RetryIntervals = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    // Üretimde de testte de AYNI olan kısım (tüketiciler + yeniden deneme). Taşıma ayrıca eklenir; test harness kendi taşımasını verir.
    public static void Configure(IBusRegistrationConfigurator x)
    {
        x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(EndpointPrefix, includeNamespace: false));
        x.AddConsumer<AppointmentCreatedConsumer>(c => c.UseMessageRetry(r => r.Intervals(RetryIntervals)));
        x.AddConsumer<AppointmentStatusChangedConsumer>(c => c.UseMessageRetry(r => r.Intervals(RetryIntervals)));
        x.AddConsumer<AppointmentRescheduledConsumer>(c => c.UseMessageRetry(r => r.Intervals(RetryIntervals)));
        x.AddConsumer<OrderPlacedConsumer>(c => c.UseMessageRetry(r => r.Intervals(RetryIntervals)));
        x.AddConsumer<OrderCancelledConsumer>(c => c.UseMessageRetry(r => r.Intervals(RetryIntervals)));
    }

    public static IServiceCollection AddNotificationMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddMassTransit(x =>
        {
            Configure(x);
            var host = configuration["RabbitMq:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                // RabbitMq:Host boşsa (testler, RabbitMQ'suz geliştirme) bellek içi taşıma.
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
