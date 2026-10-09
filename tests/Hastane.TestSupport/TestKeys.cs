using System.Security.Cryptography;
using Hastane.Shared.Security;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.TestSupport;

// Yalnız testlerin RSA çifti: süreç başına bir kez üretilir, hiçbir yere yazılmaz.
// Üretim anahtarı (jwt-public.pem + Identity user-secrets) testlerde KULLANILMAZ.
public static class TestKeys
{
    private static readonly Lazy<RSA> Rsa = new(() => RSA.Create(2048));

    public static string PrivateKeyPem => Rsa.Value.ExportPkcs8PrivateKeyPem();

    public static string PublicKeyPem => Rsa.Value.ExportSubjectPublicKeyInfoPem();

    public static string KeyId => JwtKeys.ComputeKeyId(Rsa.Value);

    // İmzalama anahtarı (özel kısım dahil).
    public static RsaSecurityKey SigningKey => new(Rsa.Value) { KeyId = KeyId };

    // Doğrulama anahtarı (yalnız açık kısım).
    public static RsaSecurityKey ValidationKey => JwtKeys.CreatePublicKey(PublicKeyPem);
}
