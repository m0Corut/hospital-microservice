using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Hastane.Appointment.Tests;

public enum NotificationMode
{
    Normal,   // 201
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
    Unauthorized, // 401: anahtar uyuşmazlığı
}

// Gönderilen bildirimin kaydı. Key: X-Internal-Key başlığı (yoksa null).
public sealed record NotificationCall(
    string Path, string? Key, string? UserId, string? DoctorId, string Title, string Message, string? Link, string Kind);

// Notification servisinin sahte hali (FakeDoctorService deseni): gerçek ağ yok. Factory başına TEK örnek.
// Varsayılan Normal: mevcut testler bildirim kapalı/açık demeden çalışır, hiçbirini bozmaz.
public sealed class FakeNotificationService
{
    // Sahte sunucunun sahte adresi: factory bu adrese göre istekleri Doctor/Notification sahtesine ayırır.
    public const string Host = "notification.test";

    private readonly ConcurrentQueue<NotificationCall> _calls = new();

    public NotificationMode Mode { get; set; } = NotificationMode.Normal;

    public IReadOnlyList<NotificationCall> Calls => _calls.ToArray();

    public void Reset() => Mode = NotificationMode.Normal;

    // Bu randevuya (bağlantısındaki id ile) giden bildirimler.
    public IReadOnlyList<NotificationCall> CallsFor(Guid appointmentId)
        => _calls.Where(c => c.Link is not null && c.Link.EndsWith("/" + appointmentId, StringComparison.OrdinalIgnoreCase)).ToArray();

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FakeNotificationService owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            string? Str(string name) => doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;

            request.Headers.TryGetValues("X-Internal-Key", out var keys);
            owner._calls.Enqueue(new NotificationCall(
                request.RequestUri!.AbsolutePath, keys?.SingleOrDefault(),
                Str("userId"), Str("doctorId"), Str("title") ?? "", Str("message") ?? "", Str("link"), Str("kind") ?? ""));

            switch (owner.Mode)
            {
                case NotificationMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case NotificationMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
                case NotificationMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                case NotificationMode.Unauthorized: return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}

// Tek primary handler: istek Notification sahte adresine gidiyorsa Notification'a, değilse Doctor sahtesine.
// (ConfigureHttpClientDefaults tüm typed istemcilere aynı handler'ı verir; ayrım adrese göre yapılır.)
public sealed class RoutingFakeHandler(FakeDoctorService doctor, FakeNotificationService notification) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _doctor = new(doctor.CreateHandler());
    private readonly HttpMessageInvoker _notification = new(notification.CreateHandler());

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => string.Equals(request.RequestUri!.Host, FakeNotificationService.Host, StringComparison.OrdinalIgnoreCase)
            ? _notification.SendAsync(request, ct)
            : _doctor.SendAsync(request, ct);
}
