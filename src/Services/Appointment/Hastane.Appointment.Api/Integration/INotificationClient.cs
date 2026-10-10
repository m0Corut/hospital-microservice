namespace Hastane.Appointment.Api.Integration;

// Notification servisinin POST /api/internal/notifications gövdesi. UserId ile DoctorId'den tam biri dolu.
public record NotificationRequest(string? UserId, string? DoctorId, string Title, string Message, string? Link, string Kind);

// Bildirim "iyi olur" işidir, işin kendisi değil: SendAsync ASLA fırlatmaz (hata loglanır, yutulur).
public interface INotificationClient
{
    Task SendAsync(IReadOnlyList<NotificationRequest> notifications);
}
