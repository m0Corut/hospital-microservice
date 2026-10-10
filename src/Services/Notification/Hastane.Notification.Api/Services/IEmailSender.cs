namespace Hastane.Notification.Api.Services;

public record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody);

// SMTP'nin arkasındaki ince kapı: testte sahtesi takılır, gerçek SMTP'ye gidilmez.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
