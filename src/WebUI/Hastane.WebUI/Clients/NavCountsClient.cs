using Hastane.WebUI.Infrastructure.Gateway;

namespace Hastane.WebUI.Clients;

// Üst menü rozetleri (zil + mesajlar). Bir istekte menü birden çok yerde çizilir (üst menü, kenar menü); sayı istek başına
// BİR kez sorulur (scoped önbellek). Servis kapalı ya da yetkisizse null: rozet gizlenir, sayfa bozulmaz.
public interface INavCountsClient
{
    Task<long?> UnreadNotificationsAsync(CancellationToken ct);

    Task<int?> UnreadMessagesAsync(CancellationToken ct);
}

public sealed class NavCountsClient(INotificationClient notifications, IChatClient chat) : INavCountsClient
{
    private Task<long?>? _notifications;
    private Task<int?>? _messages;

    public Task<long?> UnreadNotificationsAsync(CancellationToken ct) => _notifications ??= LoadNotificationsAsync(ct);

    public Task<int?> UnreadMessagesAsync(CancellationToken ct) => _messages ??= LoadMessagesAsync(ct);

    private async Task<long?> LoadNotificationsAsync(CancellationToken ct)
    {
        // pageSize=1: yalnız unreadCount lazım, liste değil.
        var result = await notifications.GetMineAsync(true, 1, 1, ApiFailures.All, ct);
        return result.TryGetValue(out var page) ? page.UnreadCount : null;
    }

    private async Task<int?> LoadMessagesAsync(CancellationToken ct)
    {
        var result = await chat.GetUnreadAsync(ApiFailures.All, ct);
        return result.TryGetValue(out var total) ? total.Total : null;
    }
}
