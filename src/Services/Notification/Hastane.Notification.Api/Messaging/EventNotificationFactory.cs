using System.Globalization;
using Hastane.Contracts;
using Hastane.Notification.Api.Dtos;

namespace Hastane.Notification.Api.Messaging;

// Bir olaydan üretilecek bildirimin tarifi. SourceKey = "{EventId}:{alıcı}": aynı olay iki kez gelirse aynı anahtar
// tekil indekste çakışır ve ikinci bildirim yazılmaz (idempotent tüketim).
public sealed record EventNotification(string SourceKey, CreateNotificationDto Dto);

// Olay -> bildirim metni/alıcı kuralları. Metinler eski senkron yoldakiyle (Appointment.NotificationClient) AYNI;
// saf fonksiyonlar olduğundan veritabanı ya da MassTransit olmadan test edilir.
public static class EventNotificationFactory
{
    public const string KindCreated = "appointment-created";
    public const string KindStatus = "appointment-status";
    public const string KindRescheduled = "appointment-rescheduled";
    public const string KindOrderPlaced = "order-placed";
    public const string KindOrderCancelled = "order-cancelled";

    private static string PatientLink(Guid appointmentId) => $"/Patient/Appointments/Details/{appointmentId}";
    private static string DoctorLink(Guid appointmentId) => $"/Doctor/Appointments/Details/{appointmentId}";

    // Mesajlarda gösterilen saat klinik saat dilimindedir (UTC değil).
    public static string LocalDate(DateTime utc, TimeZoneInfo zone)
    {
        var asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    // Hastaya "alındı", doktora "yeni talep" (doktor doctorId ile adreslenir).
    public static IReadOnlyList<EventNotification> For(AppointmentCreated e, TimeZoneInfo zone)
    {
        var date = LocalDate(e.AppointmentDate, zone);
        return
        [
            ToUser(e.EventId, "patient", e.PatientUserId, "Randevunuz alındı", $"Randevunuz alındı: {date}", PatientLink(e.AppointmentId), KindCreated),
            ToDoctor(e.EventId, "doctor", e.DoctorId, "Yeni randevu talebi", $"Yeni randevu talebi: {date}", DoctorLink(e.AppointmentId), KindCreated),
        ];
    }

    public static IReadOnlyList<EventNotification> For(AppointmentRescheduled e, TimeZoneInfo zone)
    {
        var date = LocalDate(e.NewDate, zone);
        return
        [
            ToUser(e.EventId, "patient", e.PatientUserId, "Randevunuz yeniden planlandı", $"Randevunuz yeniden planlandı: {date}", PatientLink(e.AppointmentId), KindRescheduled),
            ToDoctor(e.EventId, "doctor", e.DoctorId, "Randevu yeniden planlandı", $"Randevu yeniden planlandı: {date}", DoctorLink(e.AppointmentId), KindRescheduled),
        ];
    }

    // Karşı tarafa: hasta değiştirdiyse doktora, doktor/Admin değiştirdiyse hastaya.
    public static IReadOnlyList<EventNotification> For(AppointmentStatusChanged e, TimeZoneInfo zone)
    {
        var date = LocalDate(e.AppointmentDate, zone);
        if (string.Equals(e.ChangedByRole, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            return [ToDoctor(e.EventId, "doctor", e.DoctorId, "Randevu durumu değişti", $"Hasta randevuyu iptal etti: {date}", DoctorLink(e.AppointmentId), KindStatus)];
        }

        return [ToUser(e.EventId, "patient", e.PatientUserId, "Randevu durumu değişti", $"Randevunuz {StatusText(e.NewStatus)}: {date}", PatientLink(e.AppointmentId), KindStatus)];
    }

    public static EventNotification For(OrderPlaced e)
        => ToUser(e.EventId, "patient", e.PatientUserId, "Siparişiniz alındı", $"Siparişiniz alındı: {e.OrderNumber}", OrderLink(e.OrderId), KindOrderPlaced);

    public static EventNotification For(OrderCancelled e)
        => ToUser(e.EventId, "patient", e.PatientUserId, "Siparişiniz iptal edildi", $"Siparişiniz iptal edildi: {e.OrderNumber}", OrderLink(e.OrderId), KindOrderCancelled);

    private static string OrderLink(Guid orderId) => $"/Patient/Orders/Details/{orderId}";

    private static string StatusText(string status) => status switch
    {
        "Confirmed" => "onaylandı",
        "Completed" => "tamamlandı",
        "Cancelled" => "iptal edildi",
        _ => "güncellendi",
    };

    private static EventNotification ToUser(Guid eventId, string who, string userId, string title, string message, string link, string kind)
        => new($"{eventId:N}:{who}", new CreateNotificationDto { UserId = userId, Title = title, Message = message, Link = link, Kind = kind });

    private static EventNotification ToDoctor(Guid eventId, string who, string doctorId, string title, string message, string link, string kind)
        => new($"{eventId:N}:{who}", new CreateNotificationDto { DoctorId = doctorId, Title = title, Message = message, Link = link, Kind = kind });
}
