using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Notification.Api.Dtos;

namespace Hastane.Notification.Tests;

internal static class NotificationTestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // İç uç isteği: key null ise X-Internal-Key başlığı hiç eklenmez.
    public static HttpRequestMessage InternalRequest(string url, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        if (key is not null) request.Headers.Add("X-Internal-Key", key);
        return request;
    }

    public static object ValidNotification(string? userId = null, string? doctorId = null, string? link = "/Patient/Appointments/Details/1")
        => new { userId, doctorId, title = "Başlık", message = "Mesaj", link, kind = "appointment-created" };

    public static object ValidEmail() => new { to = "hasta@example.com", subject = "Konu", textBody = "Merhaba" };

    public static async Task<ResultNotificationDto> CreateNotificationAsync(
        HttpClient client, string? userId, string? doctorId, string title = "Başlık", CancellationToken ct = default)
    {
        using var request = InternalRequest("/api/internal/notifications",
            new { userId, doctorId, title, message = "Mesaj", link = "/x", kind = "info" }, NotificationApiFactory.TestInternalKey);
        var response = await client.SendAsync(request, ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultNotificationDto>(Json, ct))!;
    }
}
