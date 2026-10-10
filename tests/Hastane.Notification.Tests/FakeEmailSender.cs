using System.Collections.Concurrent;
using Hastane.Notification.Api.Services;

namespace Hastane.Notification.Tests;

// Gerçek SMTP'ye gitmeyen gönderen: giden iletiler Sent'te toplanır, Failure verilirse gönderim o hatayla patlar.
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    // null = başarılı; dolu = SendAsync bu istisnayı fırlatır (SMTP kapalı/ret simülasyonu).
    public Exception? Failure { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        if (Failure is not null) throw Failure;
        return Task.CompletedTask;
    }
}
