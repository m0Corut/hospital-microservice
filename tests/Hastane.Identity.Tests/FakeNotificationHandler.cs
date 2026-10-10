using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Hastane.Identity.Tests;

public record CapturedEmail(string To, string Subject, string TextBody, string? HtmlBody, string? InternalKey, string Path);

// Sahte Notification: Identity'nin servisten servise e-posta isteğini yakalar. Fail=true -> servis kapalı gibi (bağlantı hatası).
public sealed class FakeNotificationHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<CapturedEmail> _emails = new();

    public bool Fail { get; set; }

    public IReadOnlyList<CapturedEmail> Emails => _emails.ToArray();

    public IReadOnlyList<CapturedEmail> EmailsTo(string to)
        => _emails.Where(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Fail) throw new HttpRequestException("Notification kapalı (test).");

        var json = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        request.Headers.TryGetValues("X-Internal-Key", out var keys);
        _emails.Enqueue(new CapturedEmail(
            root.GetProperty("to").GetString()!,
            root.GetProperty("subject").GetString()!,
            root.GetProperty("textBody").GetString()!,
            root.TryGetProperty("htmlBody", out var html) && html.ValueKind == JsonValueKind.String ? html.GetString() : null,
            keys?.FirstOrDefault(),
            request.Method + " " + request.RequestUri!.AbsolutePath));
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }

    // E-posta istek dışında (arka planda) gönderilir: gelene kadar kısa aralıklarla bekler.
    public async Task<CapturedEmail> WaitForEmailAsync(string to, int count = 1, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var found = EmailsTo(to);
            if (found.Count >= count) return found[count - 1];
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"{to} için {count}. e-posta gelmedi.");
    }

    // Dispose'u yutar: IHttpClientFactory elden çıkardığı işleyiciyi kapatmasın, aynı örnek sonraki istemcilerde de kullanılsın.
    protected override void Dispose(bool disposing) { }
}
