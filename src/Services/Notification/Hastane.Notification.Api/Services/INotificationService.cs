using Hastane.Notification.Api.Dtos;

namespace Hastane.Notification.Api.Services;

public interface INotificationService
{
    Task<ResultNotificationDto> CreateAsync(CreateNotificationDto dto, CancellationToken ct);

    // Olay tüketicisi için IDEMPOTENT oluşturma: sourceKey daha önce yazıldıysa hiçbir şey yapmaz ve false döner.
    Task<bool> CreateFromEventAsync(CreateNotificationDto dto, string sourceKey, CancellationToken ct);

    // userId/doctorId token'dan; ikisi de boşsa boş sayfa.
    Task<NotificationPageDto> GetMineAsync(string? userId, string? doctorId, bool unreadOnly, int page, int pageSize, CancellationToken ct);

    // Başkasının ya da olmayan bildirim: false (404).
    Task<bool> MarkReadAsync(string id, string? userId, string? doctorId, CancellationToken ct);

    Task MarkAllReadAsync(string? userId, string? doctorId, CancellationToken ct);

    // true = gönderildi, false = SMTP hatası (günlükte success=false).
    Task<bool> SendEmailAsync(SendEmailDto dto, CancellationToken ct);
}
