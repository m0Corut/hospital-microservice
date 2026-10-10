using System.Security.Cryptography;
using Hastane.Shared.Security;
using Microsoft.Extensions.Options;

namespace Hastane.Identity.Api.Services;

// Identity token ÜRETTİĞİ için özel anahtarı zorunlu kılar (diğer servislerde bu kural yok).
// Eksik/bozuk anahtarla uygulama ayağa kalkmaz: "çalışıyor ama kimse giriş yapamıyor" durumu olmasın.
public class IdentityJwtOptionsValidator : IValidateOptions<JwtOptions>
{
    // Jwt:PrivateKeyPemFile verildiyse dosya, AddHastaneJwtAuthentication'ın PostConfigure adımında zaten
    // PrivateKeyPem'e okunmuştur; doğrulayıcı her iki kaynağı da bu tek alandan görür.
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PrivateKeyPem))
            return ValidateOptionsResult.Fail(
                "Jwt:PrivateKeyPem yapılandırılmamış. Geliştirmede 'scripts/new-jwt-keys.ps1' çalıştırın; " +
                "üretimde Jwt__PrivateKeyPem ortam değişkenini ya da Jwt__PrivateKeyPemFile (Docker secret yolu) verin.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.PrivateKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return ValidateOptionsResult.Fail("Jwt:PrivateKeyPem geçerli bir PEM özel anahtarı değil (PKCS#8 RSA beklenir).");
        }

        if (options.AccessTokenMinutes <= 0)
            return ValidateOptionsResult.Fail("Jwt:AccessTokenMinutes sıfırdan büyük olmalı.");

        return ValidateOptionsResult.Success;
    }
}
