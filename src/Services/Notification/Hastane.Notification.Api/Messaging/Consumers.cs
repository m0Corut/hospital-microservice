using Hastane.Contracts;
using Hastane.Notification.Api.Services;
using Hastane.Notification.Api.Settings;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Hastane.Notification.Api.Messaging;

// ÖĞRETİM: RabbitMQ en az BİR KEZ teslim eder; yani aynı olay iki kez gelebilir (tüketici işi bitirip onay vermeden çökerse,
// ağ onayı kaybederse...). Çözüm "tek sefer teslim" aramak değil, tüketiciyi IDEMPOTENT yapmaktır: her bildirimin SourceKey'i
// ("{EventId}:{alıcı}") Mongo'da tekil indekslidir, ikinci yazım çakışır ve sessizce atlanır. Ayrıca fan-out güvenli:
// iki bildirimden yalnız biri yazılıp süreç ölürse, yeniden teslimde yalnız eksik olan yazılır.
public abstract class NotificationConsumerBase(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
{
    protected TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(scheduling.Value.TimeZone);

    protected async Task WriteAsync(IEnumerable<EventNotification> items, CancellationToken ct)
    {
        foreach (var item in items)
            await notifications.CreateFromEventAsync(item.Dto, item.SourceKey, ct);
    }
}

public class AppointmentCreatedConsumer(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
    : NotificationConsumerBase(notifications, scheduling), IConsumer<AppointmentCreated>
{
    public Task Consume(ConsumeContext<AppointmentCreated> context)
        => WriteAsync(EventNotificationFactory.For(context.Message, Zone), context.CancellationToken);
}

public class AppointmentStatusChangedConsumer(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
    : NotificationConsumerBase(notifications, scheduling), IConsumer<AppointmentStatusChanged>
{
    public Task Consume(ConsumeContext<AppointmentStatusChanged> context)
        => WriteAsync(EventNotificationFactory.For(context.Message, Zone), context.CancellationToken);
}

public class AppointmentRescheduledConsumer(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
    : NotificationConsumerBase(notifications, scheduling), IConsumer<AppointmentRescheduled>
{
    public Task Consume(ConsumeContext<AppointmentRescheduled> context)
        => WriteAsync(EventNotificationFactory.For(context.Message, Zone), context.CancellationToken);
}

public class OrderPlacedConsumer(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
    : NotificationConsumerBase(notifications, scheduling), IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context)
        => WriteAsync([EventNotificationFactory.For(context.Message)], context.CancellationToken);
}

public class OrderCancelledConsumer(INotificationService notifications, IOptions<SchedulingOptions> scheduling)
    : NotificationConsumerBase(notifications, scheduling), IConsumer<OrderCancelled>
{
    public Task Consume(ConsumeContext<OrderCancelled> context)
        => WriteAsync([EventNotificationFactory.For(context.Message)], context.CancellationToken);
}
