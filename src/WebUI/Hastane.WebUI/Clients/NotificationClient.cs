using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Notifications;

namespace Hastane.WebUI.Clients;

// Bildirimler (Notification servisi). Alıcı token'dan bulunur; WebUI kimlik göndermez.
public interface INotificationClient
{
    Task<ApiResult<NotificationPageApiModel>> GetMineAsync(bool unreadOnly, int page, int pageSize, ApiFailures handle, CancellationToken ct);

    Task<ApiResult> MarkReadAsync(string id, ApiFailures handle, CancellationToken ct);

    Task<ApiResult> MarkAllReadAsync(CancellationToken ct);
}

public sealed class NotificationClient(IGatewayClient gateway) : INotificationClient
{
    public Task<ApiResult<NotificationPageApiModel>> GetMineAsync(bool unreadOnly, int page, int pageSize, ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<NotificationPageApiModel>(
            $"/gateway/notifications/me?unreadOnly={(unreadOnly ? "true" : "false")}&page={page}&pageSize={pageSize}", handle, ct);

    public Task<ApiResult> MarkReadAsync(string id, ApiFailures handle, CancellationToken ct)
        => gateway.PostAsync($"/gateway/notifications/{Uri.EscapeDataString(id)}/read", null, handle, ct);

    public Task<ApiResult> MarkAllReadAsync(CancellationToken ct)
        => gateway.PostAsync("/gateway/notifications/read-all", null, ct: ct);
}
