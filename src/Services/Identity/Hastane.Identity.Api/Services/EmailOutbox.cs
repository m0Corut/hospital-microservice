using System.Threading.Channels;
using Hastane.Identity.Api.Integration;

namespace Hastane.Identity.Api.Services;

// E-posta gönderimi İSTEK DIŞINDA yapılır: şifremi unuttum yanıtı Notification'ın hızına/durumuna bağlı olmasın
// (kullanıcı var/yok zamanlama farkı ve 3 sn'lik bekleme sızıntısı olmasın). Bellek içi, sınırlı kuyruk;
// süreç çökerse bekleyen e-postalar kaybolur (kullanıcı yeniden ister) - bilinçli kabul.
public interface IEmailOutbox
{
    bool TryEnqueue(EmailMessage message);
}

public sealed class EmailOutbox : IEmailOutbox
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    public ChannelReader<EmailMessage> Reader => _channel.Reader;
}

public sealed class EmailOutboxWorker(EmailOutbox outbox, IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in outbox.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var client = scope.ServiceProvider.GetRequiredService<INotificationEmailClient>();
                    await client.SendAsync(message, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError("E-posta gönderimi beklenmedik şekilde başarısız: {ExceptionType}", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // kapanış
        }
    }
}
