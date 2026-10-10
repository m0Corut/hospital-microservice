namespace Hastane.Shared.Security;

// "Jwt" yapılandırma bölümü. Issuer/Audience/AccessTokenMinutes sır değildir; PrivateKeyPem SIRDIR
// ve yalnız Identity'de (user-secrets / ortam değişkeni) dolu olur.
public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string DefaultIssuer = "hastane-identity";
    public const string DefaultAudience = "hastane-api";

    public string Issuer { get; set; } = DefaultIssuer;
    public string Audience { get; set; } = DefaultAudience;
    public int AccessTokenMinutes { get; set; } = 30;

    // Yalnız Identity: imzalama (özel) anahtarı, PKCS#8 PEM.
    public string? PrivateKeyPem { get; set; }

    // İsteğe bağlı ezme: doğrulama (açık) anahtarı. Boşsa derlemeye gömülü jwt-public.pem kullanılır.
    // Testlerde test anahtarı buradan girer; anahtar değişiminde (rotation) yeniden derleme gerekmesin diye de var.
    public string? PublicKeyPem { get; set; }

    // Docker secrets (/run/secrets/...) gibi dosyadan okuma. Yalnız karşılık gelen PEM değeri BOŞSA kullanılır
    // (değer her zaman dosyayı ezer). Okuma AddHastaneJwtAuthentication'daki PostConfigure'da yapılır.
    public string? PrivateKeyPemFile { get; set; }

    public string? PublicKeyPemFile { get; set; }
}
