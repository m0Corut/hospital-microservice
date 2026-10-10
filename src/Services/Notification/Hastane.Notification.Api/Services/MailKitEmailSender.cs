using Hastane.Notification.Api.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Hastane.Notification.Api.Services;

// MailKit ile SMTP. Geliştirmede MailHog (localhost:1025): TLS ve kimlik doğrulama yok.
public class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var smtp = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 10_000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.None, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
