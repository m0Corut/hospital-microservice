using System.Net;
using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Identity.Api.Integration;
using Hastane.Identity.Api.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hastane.Identity.Api.Services;

public enum ResetPasswordStatus { Success, InvalidRequest, WeakPassword }

public record ResetPasswordOutcome(ResetPasswordStatus Status, IReadOnlyList<IdentityError>? Errors = null);

public interface IPasswordResetService
{
    // Sonuç dışarıya sızmaz: çağıran HER ZAMAN aynı 202'yi döner.
    Task RequestResetAsync(string email, CancellationToken ct);
    Task<ResetPasswordOutcome> ResetAsync(ResetPasswordDto dto, CancellationToken ct);
}

public class PasswordResetService(UserManager<AppUser> userManager, IForgotPasswordLimiter limiter,
    IEmailOutbox outbox, IOptions<WebUiOptions> webUi, IOptions<IdentityOptions> identityOptions,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private const string ResetPurpose = "ResetPassword"; // UserManager.ResetPasswordTokenPurpose

    public async Task RequestResetAsync(string email, CancellationToken ct)
    {
        email = email.Trim();

        // Kullanıcı var/yok için AYNI iş yapılır: sınır sayılır, token üretilir (yoksa sahte kullanıcıyla), e-posta
        // yalnız gerçek kullanıcı için KUYRUĞA girer. Gönderim istek dışında (EmailOutbox) olduğundan Notification'ın
        // hızı yanıt süresine yansımaz.
        if (!limiter.TryAcquire(email)) return;

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Zamanlama eşitleme: aynı DataProtector token üretimi, sahte (hiçbir yere kaydedilmeyen) kullanıcıyla.
            var dummy = new AppUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = email,
                Email = email,
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            _ = await userManager.GeneratePasswordResetTokenAsync(dummy);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = BuildLink(user.Email ?? email, token);
        var name = string.IsNullOrWhiteSpace(user.FirstName) ? "Merhaba" : $"Merhaba {user.FirstName}";

        var text = $"{name},\n\nŞifrenizi sıfırlamak için aşağıdaki bağlantıyı açın (30 dakika geçerlidir):\n{link}\n\n" +
                   "Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz; şifreniz değişmez.";
        var html = $"<p>{WebUtility.HtmlEncode(name)},</p>" +
                   $"<p>Şifrenizi sıfırlamak için <a href=\"{WebUtility.HtmlEncode(link)}\">bu bağlantıyı</a> açın (30 dakika geçerlidir).</p>" +
                   "<p>Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz; şifreniz değişmez.</p>";

        if (!outbox.TryEnqueue(new EmailMessage(user.Email ?? email, "Şifre sıfırlama", text, html)))
            logger.LogWarning("E-posta kuyruğu dolu; şifre sıfırlama e-postası atlandı.");
    }

    public async Task<ResetPasswordOutcome> ResetAsync(ResetPasswordDto dto, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(dto.Email.Trim());
        if (user is null)
        {
            // Zamanlama eşitleme: var olmayan kullanıcı için de bir token doğrulaması yapılır.
            var dummy = new AppUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = dto.Email,
                Email = dto.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            _ = await userManager.VerifyUserTokenAsync(dummy,
                identityOptions.Value.Tokens.PasswordResetTokenProvider, ResetPurpose, dto.Token);
            return new ResetPasswordOutcome(ResetPasswordStatus.InvalidRequest);
        }

        // Token geçersizse/süresi dolmuşsa/bu kullanıcıya ait değilse/SecurityStamp değişmişse (kullanılmış) InvalidToken döner;
        // şifre kuralları ancak token geçerliyse sınanır, yani zayıf-şifre hataları token geçerliliğini sızdırmaz.
        var result = await userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? new ResetPasswordOutcome(ResetPasswordStatus.InvalidRequest)
                : new ResetPasswordOutcome(ResetPasswordStatus.WeakPassword, result.Errors.ToList());
        }

        // Kilitli hesap için sıfırlama bir kurtarma yoludur: sayaç ve kilit kalkar. SecurityStamp'ı Identity
        // ResetPasswordAsync içinde yenilemiştir (aynı token ikinci kez çalışmaz).
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return new ResetPasswordOutcome(ResetPasswordStatus.Success);
    }

    // Kök AYARDAN gelir (WebUi:BaseUrl); istekten (Host başlığı) hiçbir şey okunmaz.
    private string BuildLink(string email, string token)
    {
        var baseUrl = webUi.Value.BaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
