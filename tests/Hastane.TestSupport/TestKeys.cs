using System.Security.Cryptography;
using Hastane.Shared.Security;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.TestSupport;

// Yalnız testlerin RSA çifti: süreç başına bir kez üretilir, hiçbir yere yazılmaz.
// Üretim anahtarı (jwt-public.pem + Identity user-secrets) testlerde KULLANILMAZ.
//
// Neden PEM metni saklanıyor, RSA nesnesi değil: onlarca test sınıfı bu anahtara aynı anda uzanır.
// Paylaşılan tek RSA nesnesini her seferinde yeniden dışa aktarmak/imzada kullanmak, tam paket koşusunda
// ara sıra farklı kid / 401 üretti (Identity testleri). Anahtar malzemesi bir kez, tip başlatıcısında
// (thread-safe) yazıya dökülür; her çağrı kendi taze RSA nesnesini kurar, paylaşılan değişken durum kalmaz.
public static class TestKeys
{
    private static readonly string Private;
    private static readonly string Public;
    private static readonly string Kid;

    static TestKeys()
    {
        using var rsa = RSA.Create(2048);
        Private = rsa.ExportPkcs8PrivateKeyPem();
        Public = rsa.ExportSubjectPublicKeyInfoPem();
        Kid = JwtKeys.ComputeKeyId(rsa);
    }

    public static string PrivateKeyPem => Private;

    public static string PublicKeyPem => Public;

    public static string KeyId => Kid;

    // İmzalama anahtarı (özel kısım dahil). Her çağrı yeni nesne.
    public static RsaSecurityKey SigningKey => JwtKeys.CreatePrivateKey(Private);

    // Doğrulama anahtarı (yalnız açık kısım).
    public static RsaSecurityKey ValidationKey => JwtKeys.CreatePublicKey(Public);
}
