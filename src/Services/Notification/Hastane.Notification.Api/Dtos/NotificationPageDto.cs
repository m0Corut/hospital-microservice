namespace Hastane.Notification.Api.Dtos;

public class NotificationPageDto
{
    public List<ResultNotificationDto> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public long UnreadCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
