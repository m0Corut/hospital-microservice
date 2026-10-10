namespace Hastane.WebUI.Models.Api.Notifications;

// Notification servisinin sayfalı yanıtının WebUI kopyası. UnreadCount sayfa/süzgeçten bağımsız toplam okunmamıştır.
public sealed class NotificationPageApiModel
{
    public List<NotificationApiModel> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public long UnreadCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class NotificationApiModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    // Sunucu göreli yol bekler ama WebUI buna güvenmez: yalnız "/" ile başlayan yerel yol bağlantı olur.
    public string? Link { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
